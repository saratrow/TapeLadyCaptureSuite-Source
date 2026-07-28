namespace TapeLadyCaptureSuite.Models;

internal sealed record CaptureDeviceInfo(int Index, string Name, string DevicePath)
{
    public override string ToString() => Name;
}

internal enum VideoInputKind
{
    Composite,
    SVideo
}
