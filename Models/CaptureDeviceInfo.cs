namespace TapeLadyCaptureSuite.Models;

internal sealed record CaptureDeviceInfo(int Index, string Name)
{
    public override string ToString() => Name;
}
