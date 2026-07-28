using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Adapts the existing OpenCV preview path to the capture-engine contract.
/// This is a transitional engine: it supplies video frames only and does not
/// expose the EZCAP embedded audio pin.
/// </summary>
internal sealed class OpenCvCaptureEngine : ICaptureEngine
{
    private readonly object _sync = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _disposed;
    private long _frameNumber;

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return _capture is { } capture && capture.IsOpened();
            }
        }
    }

    public bool IsRunning { get; private set; }

    public CaptureDevice? CurrentDevice { get; private set; }

    public event EventHandler<CaptureFrame>? VideoFrameReady;
    public event EventHandler<AudioFrame>? AudioFrameReady;
    public event EventHandler<CaptureEngineErrorEventArgs>? CaptureError;
    public event EventHandler? CaptureStopped;

    public Task<IReadOnlyList<CaptureDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<CaptureDevice> devices = DeviceService
            .GetVideoDevices()
            .Select(CaptureDevice.FromLegacy)
            .ToList();

        return Task.FromResult(devices);
    }

    public async Task OpenAsync(
        CaptureDevice device,
        VideoInputKind inputKind,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        await CloseAsync(cancellationToken);

        var legacyDevice = new CaptureDeviceInfo(
            device.DeviceIndex,
            device.Name,
            device.Id.StartsWith("index:", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : device.Id);

        // Best effort only: many analog devices do not expose a usable crossbar.
        DeviceService.TrySetVideoInput(legacyDevice, inputKind, out _);

        var capture = new VideoCapture(device.DeviceIndex, VideoCaptureAPIs.DSHOW);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException(
                "The selected video device could not be opened. Close ArcSoft, " +
                "Camera, OBS, or any other program that may be using it.");
        }

        ConfigureCapture(capture);

        lock (_sync)
        {
            _capture = capture;
            CurrentDevice = device;
            _frameNumber = 0;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_capture is null || !_capture.IsOpened())
            {
                throw new InvalidOperationException(
                    "Open a capture device before starting the capture engine.");
            }

            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            IsRunning = true;
            _runTask = Task.Run(() => CaptureLoop(_runCancellation.Token));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        CancellationTokenSource? runCancellation;

        lock (_sync)
        {
            runTask = _runTask;
            runCancellation = _runCancellation;
            _runTask = null;
            _runCancellation = null;
            IsRunning = false;
        }

        runCancellation?.Cancel();

        if (runTask is not null)
        {
            try
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TimeoutException)
            {
                // Some capture drivers can briefly block during shutdown.
            }
            catch (OperationCanceledException) when (runCancellation?.IsCancellationRequested == true)
            {
                // Expected when StopAsync cancels the capture loop.
            }
        }

        runCancellation?.Dispose();
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);

        VideoCapture? capture;
        lock (_sync)
        {
            capture = _capture;
            _capture = null;
            CurrentDevice = null;
        }

        capture?.Release();
        capture?.Dispose();
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

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
                        RaiseError(
                            "The device opened, but it is not delivering video frames. " +
                            "Make sure the VCR is playing, then try the other Input setting " +
                            "(Composite / RCA or S-Video).");
                    }

                    Thread.Sleep(20);
                    continue;
                }

                lastGoodFrame = DateTime.UtcNow;
                reportedNoSignal = false;

                using var bgrFrame = EnsureBgr24(frame);
                var stride = checked((int)bgrFrame.Step());
                var buffer = new byte[checked(stride * bgrFrame.Rows)];
                Marshal.Copy(bgrFrame.Data, buffer, 0, buffer.Length);

                var captureFrame = new CaptureFrame(
                    Interlocked.Increment(ref _frameNumber) - 1,
                    stopwatch.Elapsed,
                    bgrFrame.Cols,
                    bgrFrame.Rows,
                    stride,
                    CapturePixelFormat.Bgr24,
                    buffer);

                VideoFrameReady?.Invoke(this, captureFrame);
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RaiseError(ex.Message, ex);
            }
        }
        finally
        {
            IsRunning = false;
            CaptureStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Mat EnsureBgr24(Mat source)
    {
        if (source.Type() == MatType.CV_8UC3)
        {
            return source.Clone();
        }

        var converted = new Mat();
        var conversion = source.Channels() switch
        {
            1 => ColorConversionCodes.GRAY2BGR,
            4 => ColorConversionCodes.BGRA2BGR,
            _ => throw new NotSupportedException(
                $"Unsupported OpenCV frame format: {source.Type()}.")
        };

        Cv2.CvtColor(source, converted, conversion);
        return converted;
    }

    private static void ConfigureCapture(VideoCapture capture)
    {
        capture.Set(
            VideoCaptureProperties.FourCC,
            ('Y') | ('U' << 8) | ('Y' << 16) | ('2' << 24));
        capture.Set(VideoCaptureProperties.FrameWidth, 720);
        capture.Set(VideoCaptureProperties.FrameHeight, 480);
        capture.Set(VideoCaptureProperties.Fps, 30.0);
        capture.Set(VideoCaptureProperties.BufferSize, 1);
    }

    private void RaiseError(string message, Exception? exception = null)
    {
        CaptureError?.Invoke(
            this,
            new CaptureEngineErrorEventArgs(message, exception));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            CloseAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Application shutdown should continue even if a driver misbehaves.
        }

        GC.SuppressFinalize(this);
    }
}
