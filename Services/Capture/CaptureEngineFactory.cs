namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Central creation point for capture engines. Milestone 6.1 establishes the
/// seam only; concrete OpenCV and DirectShow engines are added in later steps.
/// </summary>
internal static class CaptureEngineFactory
{
    public static ICaptureEngine Create(CaptureEngineKind engineKind)
    {
        return engineKind switch
        {
            CaptureEngineKind.OpenCv => new OpenCvCaptureEngine(),
            CaptureEngineKind.DirectShow => throw new NotSupportedException(
                "The DirectShow capture engine has not been added yet."),
            _ => throw new ArgumentOutOfRangeException(nameof(engineKind), engineKind, null)
        };
    }
}

internal enum CaptureEngineKind
{
    OpenCv,
    DirectShow
}
