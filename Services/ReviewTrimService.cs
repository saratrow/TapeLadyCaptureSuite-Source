using System.Diagnostics;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services;

internal sealed record TrimRequest(
    CaptureHistoryItem HistoryItem,
    TimeSpan Start,
    TimeSpan End,
    TrimMethod Method,
    double OriginalDurationSeconds);

internal sealed record TrimResult(
    string BackupPath,
    double FinalDurationSeconds);

internal sealed class ReviewTrimService
{
    public async Task<TrimResult> TrimAsync(
        string ffmpegPath,
        TrimRequest request,
        CancellationToken cancellationToken)
    {
        string sourcePath = request.HistoryItem.OutputPath;
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The completed recording is no longer available.", sourcePath);
        }

        if (request.Start < TimeSpan.Zero || request.End <= request.Start)
        {
            throw new InvalidOperationException("Trim Start must be before Trim End.");
        }

        string folder = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("The recording folder is invalid.");
        string extension = Path.GetExtension(sourcePath);
        string temporaryPath = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(sourcePath)}.trim-{Guid.NewGuid():N}{extension}");
        string? backupPath = null;

        try
        {
            await RunFfmpegAsync(ffmpegPath, sourcePath, temporaryPath, request, cancellationToken);
            VerifyOutput(temporaryPath);

            string originalsFolder = Path.Combine(folder, "Originals");
            Directory.CreateDirectory(originalsFolder);
            backupPath = CreateUniquePath(originalsFolder, Path.GetFileName(sourcePath));

            File.Move(sourcePath, backupPath);
            try
            {
                File.Move(temporaryPath, sourcePath);
            }
            catch
            {
                if (!File.Exists(sourcePath) && File.Exists(backupPath))
                {
                    File.Move(backupPath, sourcePath);
                }

                throw;
            }

            return new TrimResult(backupPath, Math.Max(0, (request.End - request.Start).TotalSeconds));
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static async Task RunFfmpegAsync(
        string ffmpegPath,
        string sourcePath,
        string temporaryPath,
        TrimRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");

        if (request.Method == TrimMethod.FastLossless)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(FormatTime(request.Start));
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(FormatTime(request.End - request.Start));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("copy");
            startInfo.ArgumentList.Add("-avoid_negative_ts");
            startInfo.ArgumentList.Add("make_zero");
        }
        else
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(FormatTime(request.Start));
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(FormatTime(request.End - request.Start));
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:v:0");
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:a:0?");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libx264");
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("veryfast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("18");
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("aac");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add("160k");
        }

        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add(temporaryPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not start the trim operation.");
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg could not trim the completed recording. {error}");
        }
    }

    private static void VerifyOutput(string temporaryPath)
    {
        if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
        {
            throw new InvalidOperationException("FFmpeg completed, but did not produce a usable trimmed file.");
        }
    }

    private static string CreateUniquePath(string folder, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string candidate = Path.Combine(folder, fileName);
        int suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{stem} ({suffix++}){extension}");
        }

        return candidate;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal sealed class ReviewWorkQueue
{
    private readonly object _sync = new();
    private readonly Queue<QueuedTrimRequest> _pending = [];
    private readonly ReviewTrimService _trimService = new();
    private readonly Func<bool> _isCaptureActive;
    private CancellationTokenSource? _currentCancellation;
    private bool _processing;

    public ReviewWorkQueue(Func<bool> isCaptureActive)
    {
        _isCaptureActive = isCaptureActive;
    }

    public event EventHandler<QueuedTrimCompletedEventArgs>? Completed;

    public void Enqueue(QueuedTrimRequest request)
    {
        lock (_sync)
        {
            _pending.Enqueue(request);
        }

        if (!_isCaptureActive())
        {
            _ = ProcessAvailableAsync();
        }
    }

    public void CaptureStateChanged()
    {
        if (_isCaptureActive())
        {
            lock (_sync)
            {
                _currentCancellation?.Cancel();
            }
        }
        else
        {
            _ = ProcessAvailableAsync();
        }
    }

    private async Task ProcessAvailableAsync()
    {
        lock (_sync)
        {
            if (_processing || _pending.Count == 0)
            {
                return;
            }

            _processing = true;
        }

        try
        {
            while (true)
            {
                QueuedTrimRequest request;
                lock (_sync)
                {
                    if (_pending.Count == 0 || _isCaptureActive())
                    {
                        break;
                    }

                    request = _pending.Dequeue();
                    _currentCancellation = new CancellationTokenSource();
                }

                try
                {
                    if (_isCaptureActive())
                    {
                        lock (_sync)
                        {
                            _pending.Enqueue(request);
                        }
                        break;
                    }

                    TrimResult result = await _trimService.TrimAsync(
                        request.FfmpegPath,
                        request.TrimRequest,
                        _currentCancellation.Token);
                    Completed?.Invoke(this, new QueuedTrimCompletedEventArgs(request, result, null));
                }
                catch (OperationCanceledException) when (_isCaptureActive())
                {
                    lock (_sync)
                    {
                        _pending.Enqueue(request);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Completed?.Invoke(this, new QueuedTrimCompletedEventArgs(request, null, ex));
                }
                finally
                {
                    lock (_sync)
                    {
                        _currentCancellation?.Dispose();
                        _currentCancellation = null;
                    }
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _processing = false;
            }
        }
    }
}

internal sealed record QueuedTrimRequest(string FfmpegPath, TrimRequest TrimRequest);

internal sealed class QueuedTrimCompletedEventArgs : EventArgs
{
    public QueuedTrimCompletedEventArgs(QueuedTrimRequest request, TrimResult? result, Exception? error)
    {
        Request = request;
        Result = result;
        Error = error;
    }

    public QueuedTrimRequest Request { get; }
    public TrimResult? Result { get; }
    public Exception? Error { get; }
}