using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TapeLadyCaptureSuite.Services;

internal sealed class RecordingService : IDisposable
{
    private readonly object _sync = new();
    private readonly List<string> _segments = [];
    private readonly Queue<string> _recentErrors = new();
    private Process? _process;
    private Task? _previewTask;
    private Task? _errorTask;
    private CancellationTokenSource? _cancellation;
    private string? _ffmpegPath;
    private string? _videoDevice;
    private string? _audioDevice;
    private string? _sessionFolder;
    private string? _finalOutputPath;
    private bool _stopping;
    private int _segmentNumber;
    private bool _disposed;

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public string? FinalOutputPath => _finalOutputPath;
    public long FramesCaptured { get; private set; }
    public long DroppedFrames { get; private set; }

    public event EventHandler<Bitmap>? PreviewFrameReady;
    public event EventHandler? StatisticsChanged;
    public event EventHandler<string>? RecordingError;

    public async Task StartSessionAsync(
        string ffmpegPath,
        string videoDevice,
        string? audioDevice,
        string finalOutputPath)
    {
        ThrowIfDisposed();

        if (IsRecording || IsPaused)
        {
            throw new InvalidOperationException("A recording session is already active.");
        }

        _ffmpegPath = ffmpegPath;
        _videoDevice = videoDevice;
        _audioDevice = string.IsNullOrWhiteSpace(audioDevice) ? null : audioDevice;
        _finalOutputPath = finalOutputPath;

        var parent = Path.GetDirectoryName(finalOutputPath)
            ?? throw new InvalidOperationException("The save folder is invalid.");

        Directory.CreateDirectory(parent);

        _sessionFolder = Path.Combine(
            parent,
            $".tlcapture_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");

        Directory.CreateDirectory(_sessionFolder);

        _segments.Clear();
        _recentErrors.Clear();
        _segmentNumber = 0;
        FramesCaptured = 0;
        DroppedFrames = 0;
        IsPaused = false;

        await StartSegmentAsync();
        IsRecording = true;
    }

    public async Task PauseAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        await StopCurrentProcessAsync();

        IsRecording = false;
        IsPaused = true;
    }

    public async Task ResumeAsync()
    {
        if (!IsPaused)
        {
            return;
        }

        await StartSegmentAsync();

        IsPaused = false;
        IsRecording = true;
    }

    public async Task<string> StopSessionAsync()
    {
        if (!IsRecording && !IsPaused)
        {
            throw new InvalidOperationException("There is no active recording session.");
        }

        if (IsRecording)
        {
            await StopCurrentProcessAsync();
        }

        IsRecording = false;
        IsPaused = false;

        if (string.IsNullOrWhiteSpace(_finalOutputPath))
        {
            throw new InvalidOperationException("The output filename is missing.");
        }

        if (_segments.Count == 0)
        {
            throw new InvalidOperationException(
                "No video segments were created. " + GetRecentErrorSummary());
        }

        var validSegments = _segments
            .Where(path => File.Exists(path) && new FileInfo(path).Length > 0)
            .ToList();

        if (validSegments.Count == 0)
        {
            throw new InvalidOperationException(
                "The recording did not produce a usable video file. " +
                GetRecentErrorSummary());
        }

        if (File.Exists(_finalOutputPath))
        {
            File.Delete(_finalOutputPath);
        }

        if (validSegments.Count == 1)
        {
            File.Move(validSegments[0], _finalOutputPath);
        }
        else
        {
            await ConcatenateSegmentsAsync(validSegments, _finalOutputPath);
        }

        CleanupSessionFolder();
        return _finalOutputPath;
    }

    public async Task CancelSessionAsync()
    {
        if (IsRecording)
        {
            await StopCurrentProcessAsync();
        }

        IsRecording = false;
        IsPaused = false;
        CleanupSessionFolder();
    }

    private async Task StartSegmentAsync()
    {
        if (string.IsNullOrWhiteSpace(_ffmpegPath) ||
            string.IsNullOrWhiteSpace(_videoDevice) ||
            string.IsNullOrWhiteSpace(_sessionFolder))
        {
            throw new InvalidOperationException("The recording session was not initialized.");
        }

        _segmentNumber++;
        var segmentPath = Path.Combine(
            _sessionFolder,
            $"segment_{_segmentNumber:000}.mp4");

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        AddCommonInputArguments(startInfo.ArgumentList);

        var input = $"video={_videoDevice}";
        if (!string.IsNullOrWhiteSpace(_audioDevice))
        {
            input += $":audio={_audioDevice}";
        }

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(input);

        // Main MP4 output: square-pixel 4:3 SD, deinterlaced for modern playback.
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");

        if (!string.IsNullOrWhiteSpace(_audioDevice))
        {
            startInfo.ArgumentList.Add("-map");
            startInfo.ArgumentList.Add("0:a:0?");
        }

        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(
            "yadif=0:-1:0,scale=640:480:flags=lanczos,setsar=1");

        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-preset");
        startInfo.ArgumentList.Add("veryfast");
        startInfo.ArgumentList.Add("-crf");
        startInfo.ArgumentList.Add("20");
        startInfo.ArgumentList.Add("-maxrate");
        startInfo.ArgumentList.Add("3500k");
        startInfo.ArgumentList.Add("-bufsize");
        startInfo.ArgumentList.Add("7000k");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");

        if (!string.IsNullOrWhiteSpace(_audioDevice))
        {
            startInfo.ArgumentList.Add("-c:a");
            startInfo.ArgumentList.Add("aac");
            startInfo.ArgumentList.Add("-b:a");
            startInfo.ArgumentList.Add("160k");
            startInfo.ArgumentList.Add("-ar");
            startInfo.ArgumentList.Add("48000");
            startInfo.ArgumentList.Add("-ac");
            startInfo.ArgumentList.Add("2");
        }

        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add(segmentPath);

        // A lightweight JPEG stream on stdout keeps the live preview active
        // without trying to open the capture device a second time.
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:v:0");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("fps=12,scale=640:480:flags=fast_bilinear");
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("mjpeg");
        startInfo.ArgumentList.Add("-q:v");
        startInfo.ArgumentList.Add("8");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("image2pipe");
        startInfo.ArgumentList.Add("pipe:1");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not be started.");

        var cancellation = new CancellationTokenSource();

        lock (_sync)
        {
            _process = process;
            _cancellation = cancellation;
            _stopping = false;
        }

        _segments.Add(segmentPath);
        _previewTask = Task.Run(
            () => ReadJpegPreviewAsync(process.StandardOutput.BaseStream, cancellation.Token));

        _errorTask = Task.Run(
            () => ReadErrorOutputAsync(process, cancellation.Token));

        await Task.Delay(900);

        if (process.HasExited)
        {
            await AwaitReaderTasksAsync();

            throw new InvalidOperationException(
                "FFmpeg could not open the selected capture devices. " +
                "Close ArcSoft, Camera, OBS, or other capture programs and try again. " +
                GetRecentErrorSummary());
        }
    }

    private static void AddCommonInputArguments(
        System.Collections.ObjectModel.Collection<string> arguments)
    {
        arguments.Add("-hide_banner");
        arguments.Add("-nostats");
        arguments.Add("-loglevel");
        arguments.Add("info");
        arguments.Add("-y");
        arguments.Add("-thread_queue_size");
        arguments.Add("1024");
        arguments.Add("-f");
        arguments.Add("dshow");
        arguments.Add("-rtbufsize");
        arguments.Add("512M");
    }

    private async Task StopCurrentProcessAsync()
    {
        Process? process;
        CancellationTokenSource? cancellation;

        lock (_sync)
        {
            process = _process;
            cancellation = _cancellation;
            _stopping = true;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("q");
                await process.StandardInput.FlushAsync();

                var exited = await WaitForExitWithTimeoutAsync(process, 8000);
                if (!exited && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        finally
        {
            cancellation?.Cancel();
            await AwaitReaderTasksAsync();

            process.Dispose();
            cancellation?.Dispose();

            lock (_sync)
            {
                _process = null;
                _cancellation = null;
                _stopping = false;
            }
        }
    }

    private async Task ReadErrorOutputAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                RememberErrorLine(line);
                ParseStatistics(line);
            }

            if (!_stopping && process.HasExited && process.ExitCode != 0)
            {
                RecordingError?.Invoke(
                    this,
                    "The recording engine stopped unexpectedly. " +
                    GetRecentErrorSummary());
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during stop.
        }
        catch (Exception ex)
        {
            if (!_stopping)
            {
                RecordingError?.Invoke(this, ex.Message);
            }
        }
    }

    private void ParseStatistics(string line)
    {
        var frameMatch = Regex.Match(line, @"frame=\s*(\d+)");
        if (frameMatch.Success &&
            long.TryParse(frameMatch.Groups[1].Value, out var frames))
        {
            FramesCaptured = Math.Max(FramesCaptured, frames);
        }

        var dropMatch = Regex.Match(line, @"drop=\s*(\d+)");
        if (dropMatch.Success &&
            long.TryParse(dropMatch.Groups[1].Value, out var dropped))
        {
            DroppedFrames = Math.Max(DroppedFrames, dropped);
        }

        if (frameMatch.Success || dropMatch.Success)
        {
            StatisticsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RememberErrorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_recentErrors)
        {
            _recentErrors.Enqueue(line.Trim());
            while (_recentErrors.Count > 18)
            {
                _recentErrors.Dequeue();
            }
        }
    }

    private string GetRecentErrorSummary()
    {
        lock (_recentErrors)
        {
            var useful = _recentErrors
                .Where(line =>
                    line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("could not", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("busy", StringComparison.OrdinalIgnoreCase))
                .TakeLast(4)
                .ToList();

            if (useful.Count == 0)
            {
                useful = _recentErrors.TakeLast(3).ToList();
            }

            return useful.Count == 0
                ? string.Empty
                : string.Join(" ", useful);
        }
    }

    private async Task ReadJpegPreviewAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var jpeg = new MemoryStream();
        var collecting = false;
        var previous = -1;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

                if (count == 0)
                {
                    break;
                }

                for (var index = 0; index < count; index++)
                {
                    var current = buffer[index];

                    if (!collecting)
                    {
                        if (previous == 0xFF && current == 0xD8)
                        {
                            collecting = true;
                            jpeg.SetLength(0);
                            jpeg.WriteByte(0xFF);
                            jpeg.WriteByte(0xD8);
                        }

                        previous = current;
                        continue;
                    }

                    jpeg.WriteByte(current);

                    if (previous == 0xFF && current == 0xD9)
                    {
                        EmitJpeg(jpeg);
                        collecting = false;
                        jpeg.SetLength(0);
                    }

                    previous = current;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during stop.
        }
        catch (Exception ex)
        {
            if (!_stopping)
            {
                RecordingError?.Invoke(
                    this,
                    $"Recording continued, but the live preview stopped: {ex.Message}");
            }
        }
    }

    private void EmitJpeg(MemoryStream jpeg)
    {
        try
        {
            jpeg.Position = 0;
            using var image = Image.FromStream(jpeg, useEmbeddedColorManagement: false);
            PreviewFrameReady?.Invoke(this, new Bitmap(image));
        }
        catch
        {
            // Ignore a single incomplete preview frame.
        }
    }

    private async Task ConcatenateSegmentsAsync(
        IReadOnlyList<string> segments,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(_ffmpegPath) ||
            string.IsNullOrWhiteSpace(_sessionFolder))
        {
            throw new InvalidOperationException("FFmpeg is not available for finalizing.");
        }

        var listPath = Path.Combine(_sessionFolder, "segments.txt");

        var lines = segments.Select(path =>
            $"file '{path.Replace("'", "'\\''")}'");

        await File.WriteAllLinesAsync(listPath, lines, Encoding.UTF8);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("concat");
        startInfo.ArgumentList.Add("-safe");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(listPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("FFmpeg could not finalize the recording.");

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(
                "The recording segments were captured, but could not be joined. " +
                error);
        }
    }

    private static async Task<bool> WaitForExitWithTimeoutAsync(
        Process process,
        int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task AwaitReaderTasksAsync()
    {
        var tasks = new[] { _previewTask, _errorTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(1800));
        }
        catch
        {
            // Reader tasks are best effort during shutdown.
        }

        _previewTask = null;
        _errorTask = null;
    }

    private void CleanupSessionFolder()
    {
        if (string.IsNullOrWhiteSpace(_sessionFolder))
        {
            return;
        }

        try
        {
            if (Directory.Exists(_sessionFolder))
            {
                Directory.Delete(_sessionFolder, recursive: true);
            }
        }
        catch
        {
            // A leftover hidden temp folder is safer than deleting user media.
        }

        _sessionFolder = null;
        _segments.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RecordingService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            CancelSessionAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Do not block application shutdown.
        }
    }
}
