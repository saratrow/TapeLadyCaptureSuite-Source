using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Contract for a single owner of capture hardware. Implementations may use
/// OpenCV, DirectShow, or another backend, while the UI remains backend-neutral.
/// </summary>
internal interface ICaptureEngine : IDisposable
{
    bool IsOpen { get; }

    bool IsRunning { get; }

    CaptureDevice? CurrentDevice { get; }

    event EventHandler<CaptureFrame>? VideoFrameReady;

    event EventHandler<AudioFrame>? AudioFrameReady;

    event EventHandler<CaptureEngineErrorEventArgs>? CaptureError;

    event EventHandler? CaptureStopped;

    Task<IReadOnlyList<CaptureDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default);

    Task OpenAsync(
        CaptureDevice device,
        VideoInputKind inputKind,
        CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

internal sealed class CaptureEngineErrorEventArgs : EventArgs
{
    public CaptureEngineErrorEventArgs(string message, Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("An error message is required.", nameof(message));
        }

        Message = message;
        Exception = exception;
    }

    public string Message { get; }

    public Exception? Exception { get; }
}
