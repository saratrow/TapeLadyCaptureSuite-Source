namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// A technology-neutral block of PCM audio produced by a capture engine.
/// </summary>
internal sealed class AudioFrame
{
    public AudioFrame(
        TimeSpan timestamp,
        int sampleRate,
        short channels,
        short bitsPerSample,
        byte[] buffer)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (bitsPerSample <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
        }

        ArgumentNullException.ThrowIfNull(buffer);

        Timestamp = timestamp;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        Buffer = buffer;
    }

    public TimeSpan Timestamp { get; }

    public int SampleRate { get; }

    public short Channels { get; }

    public short BitsPerSample { get; }

    public ReadOnlyMemory<byte> Buffer { get; }
}
