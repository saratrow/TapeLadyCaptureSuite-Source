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

        // Analog USB grabbers are often strict about format negotiation.
        // YUY2 at 720x480/29.97 is the normal NTSC capture format. Some
        // drivers only accept 30.0, so we use that value while preserving
        // the full 720x480 frame.
        capture.Set(VideoCaptureProperties.FourCC, ('Y') | ('U' << 8) | ('Y' << 16) | ('2' << 24));
        capture.Set(VideoCaptureProperties.FrameWidth, 720);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        capture.Set(VideoCaptureProperties.Fps, 30.0);
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
            var lastGoodFrame = DateTime.UtcNow;
            var reportedNoSignal = false;

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
                    if (!reportedNoSignal &&
                        DateTime.UtcNow - lastGoodFrame > TimeSpan.FromSeconds(3))
                    {
                        reportedNoSignal = true;
                        PreviewError?.Invoke(
                            this,
                            "The device opened, but it is not delivering video frames. " +
                            "Make sure the VCR is playing, then try the other Input setting " +
                            "(Composite / RCA or S-Video).");
                    }

                    Thread.Sleep(20);
                    continue;
                }

                lastGoodFrame = DateTime.UtcNow;
                reportedNoSignal = false;

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
