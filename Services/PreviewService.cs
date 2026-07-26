using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace TapeLadyCaptureSuite.Services;

internal sealed class PreviewService : IDisposable
{
    private readonly object _sync = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _cancellation;
    private Task? _previewTask;
    private bool _disposed;

    public bool IsRunning { get; private set; }

    public event EventHandler<Bitmap>? FrameReady;
    public event EventHandler<string>? PreviewError;
    public event EventHandler? PreviewStopped;

    public async Task StartAsync(int deviceIndex)
    {
        ThrowIfDisposed();

        await StopAsync();

        var capture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException(
                "The selected video device could not be opened. " +
                "Close ArcSoft, Camera, OBS, or any other program that may be using it.");
        }

        // NTSC SD defaults. A driver may ignore values it does not support.
        capture.Set(VideoCaptureProperties.FrameWidth, 720);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        capture.Set(VideoCaptureProperties.Fps, 29.97);
        capture.Set(VideoCaptureProperties.BufferSize, 1);

        lock (_sync)
        {
            _capture = capture;
            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            _previewTask = Task.Run(() => PreviewLoop(_cancellation.Token));
        }
    }

    private void PreviewLoop(CancellationToken cancellationToken)
    {
        try
        {
            using var frame = new Mat();

            while (!cancellationToken.IsCancellationRequested)
            {
                VideoCapture? capture;

                lock (_sync)
                {
                    capture = _capture;
                }

                if (capture is null || !capture.IsOpened())
                {
                    break;
                }

                if (!capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(20);
                    continue;
                }

                using var bitmap = BitmapConverter.ToBitmap(frame);
                FrameReady?.Invoke(this, new Bitmap(bitmap));
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                PreviewError?.Invoke(this, ex.Message);
            }
        }
        finally
        {
            IsRunning = false;
            PreviewStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? previewTask;
        VideoCapture? capture;

        lock (_sync)
        {
            cancellation = _cancellation;
            previewTask = _previewTask;
            capture = _capture;

            _cancellation = null;
            _previewTask = null;
            _capture = null;
            IsRunning = false;
        }

        if (cancellation is not null)
        {
            cancellation.Cancel();
        }

        if (previewTask is not null)
        {
            try
            {
                await Task.WhenAny(previewTask, Task.Delay(1200));
            }
            catch
            {
                // Shutdown must continue even if the driver misbehaves.
            }
        }

        capture?.Release();
        capture?.Dispose();
        cancellation?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PreviewService));
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
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Do not block application shutdown.
        }
    }
}
