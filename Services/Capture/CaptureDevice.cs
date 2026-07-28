using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Describes one physical capture device and the capabilities discovered for it.
/// </summary>
internal sealed record CaptureDevice(
    string Id,
    string Name,
    int DeviceIndex,
    IReadOnlyList<CapturePin> VideoPins,
    IReadOnlyList<CapturePin> AudioPins,
    IReadOnlySet<VideoInputKind> SupportedInputs)
{
    public static CaptureDevice FromLegacy(CaptureDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        var id = string.IsNullOrWhiteSpace(deviceInfo.DevicePath)
            ? $"index:{deviceInfo.Index}"
            : deviceInfo.DevicePath;

        return new CaptureDevice(
            id,
            deviceInfo.Name,
            deviceInfo.Index,
            Array.Empty<CapturePin>(),
            Array.Empty<CapturePin>(),
            new HashSet<VideoInputKind>());
    }

    public override string ToString() => Name;
}

internal sealed record CapturePin(
    string Id,
    string Name,
    CapturePinKind Kind);

internal enum CapturePinKind
{
    Video,
    Audio,
    Other
}
