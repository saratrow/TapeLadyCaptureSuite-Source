namespace TapeLadyCaptureSuite.Models;

internal enum AudioSourceKind
{
    None,
    WindowsDevice,
    VideoDevicePin
}

internal sealed record AudioSourceInfo(
    AudioSourceKind Kind,
    string DisplayName,
    string DeviceName,
    string? PinName = null)
{
    public override string ToString() => DisplayName;
}
