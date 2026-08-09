namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// A technology-neutral video frame produced by a capture engine.
/// The buffer is owned by this instance and must not be modified by consumers.
/// </summary>
internal sealed class CaptureFrame
{
    public CaptureFrame(
        long frameNumber,
        TimeSpan timestamp,
        int width,
        int height,
        int stride,
        CapturePixelFormat pixelFormat,
        byte[] buffer)
    {
        if (frameNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        ArgumentNullException.ThrowIfNull(buffer);

        FrameNumber = frameNumber;
        Timestamp = timestamp;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
        Buffer = buffer;
    }

    public long FrameNumber { get; }

    public TimeSpan Timestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public CapturePixelFormat PixelFormat { get; }

    public ReadOnlyMemory<byte> Buffer { get; }
}

internal enum CapturePixelFormat
{
    Unknown,
    Bgr24,
    Bgra32,
    Yuy2,
    Nv12,
    Mjpeg
}
