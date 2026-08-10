using System.Runtime.InteropServices;
using System.Text;
using DirectShowLib;

namespace TapeLadyCaptureSuite.Services.Capture;

/// <summary>
/// Read-only DirectShow diagnostics. This class does not run a graph or take
/// ownership of a device beyond the short inspection operation.
/// </summary>
internal static class DirectShowDeviceExplorer
{
    public static IReadOnlyList<DirectShowDeviceReport> InspectVideoCaptureDevices()
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        var reports = new List<DirectShowDeviceReport>(devices.Length);

        try
        {
            foreach (var device in devices)
            {
                reports.Add(InspectDevice(device));
            }
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }

        return reports;
    }

    public static string BuildTextReport()
    {
        var reports = InspectVideoCaptureDevices();
        var text = new StringBuilder();

        text.AppendLine("Tape Lady Capture Suite - DirectShow Device Report");
        text.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(new string('=', 64));

        if (reports.Count == 0)
        {
            text.AppendLine("No DirectShow video capture devices were found.");
            return text.ToString();
        }

        for (var deviceIndex = 0; deviceIndex < reports.Count; deviceIndex++)
        {
            var device = reports[deviceIndex];
            text.AppendLine($"Device {deviceIndex + 1}: {device.Name}");
            text.AppendLine($"Path: {device.DevicePath}");

            if (!string.IsNullOrWhiteSpace(device.ErrorMessage))
            {
                text.AppendLine($"Inspection error: {device.ErrorMessage}");
            }

            if (device.Pins.Count == 0)
            {
                text.AppendLine("Pins: none reported");
            }
            else
            {
                text.AppendLine("Pins:");
                foreach (var pin in device.Pins)
                {
                    text.AppendLine($"  - {pin.Name}");
                    text.AppendLine($"    Direction: {pin.Direction}");
                    text.AppendLine($"    Kind: {pin.Kind}");
                    text.AppendLine($"    Category: {pin.Category}");

                    if (pin.MediaTypes.Count == 0)
                    {
                        text.AppendLine("    Media types: none reported");
                    }
                    else
                    {
                        text.AppendLine("    Media types:");
                        foreach (var mediaType in pin.MediaTypes)
                        {
                            text.AppendLine(
                                $"      {mediaType.MajorTypeName} / {mediaType.SubTypeName} " +
                                $"(format: {mediaType.FormatTypeName}) {mediaType.Details}");
                        }
                    }

                    if (pin.VideoCapabilities.Count > 0)
                    {
                        text.AppendLine("    IAMStreamConfig video capabilities:");
                        foreach (var capability in pin.VideoCapabilities)
                        {
                            text.AppendLine($"      {capability.Description}");
                        }
                    }
                }
            }

            if (deviceIndex < reports.Count - 1)
            {
                text.AppendLine(new string('-', 64));
            }
        }

        return text.ToString();
    }

    public static string SaveTextReport(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A report destination is required.", nameof(destinationPath));
        }

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, BuildTextReport(), Encoding.UTF8);
        return fullPath;
    }

    private static DirectShowDeviceReport InspectDevice(DsDevice device)
    {
        IBaseFilter? filter = null;
        IEnumPins? pinEnumerator = null;
        var pins = new List<DirectShowPinReport>();

        try
        {
            var filterInterfaceId = typeof(IBaseFilter).GUID;
            device.Mon.BindToObject(
                null,
                null,
                ref filterInterfaceId,
                out var filterObject);

            filter = (IBaseFilter)filterObject;
            var hr = filter.EnumPins(out pinEnumerator);
            DsError.ThrowExceptionForHR(hr);

            var fetchedPins = new IPin[1];
            while (pinEnumerator.Next(1, fetchedPins, IntPtr.Zero) == 0)
            {
                var pin = fetchedPins[0];
                try
                {
                    pins.Add(InspectPin(pin));
                }
                catch (Exception ex)
                {
                    pins.Add(new DirectShowPinReport(
                        "Unreadable pin",
                        "Unknown",
                        CapturePinKind.Other,
                        "Not reported",
                        Array.Empty<DirectShowMediaTypeReport>(),
                        Array.Empty<DirectShowVideoCapabilityReport>(),
                        ex.Message));
                }
                finally
                {
                    ReleaseCom(pin);
                    fetchedPins[0] = null!;
                }
            }

            return new DirectShowDeviceReport(
                SafeName(device.Name, "Unnamed video capture device"),
                device.DevicePath ?? string.Empty,
                pins,
                null);
        }
        catch (Exception ex)
        {
            return new DirectShowDeviceReport(
                SafeName(device.Name, "Unnamed video capture device"),
                device.DevicePath ?? string.Empty,
                pins,
                ex.Message);
        }
        finally
        {
            ReleaseCom(pinEnumerator);
            ReleaseCom(filter);
        }
    }

    private static DirectShowPinReport InspectPin(IPin pin)
    {
        var name = "Unnamed pin";
        var directionName = "Unknown";
        var mediaTypes = new List<DirectShowMediaTypeReport>();
        var videoCapabilities = new List<DirectShowVideoCapabilityReport>();
        string? errorMessage = null;

        var hr = pin.QueryPinInfo(out var pinInfo);
        if (hr >= 0)
        {
            try
            {
                name = SafeName(pinInfo.name, name);
            }
            finally
            {
                ReleaseCom(pinInfo.filter);
            }
        }

        hr = pin.QueryDirection(out var direction);
        if (hr >= 0)
        {
            directionName = direction == PinDirection.Input ? "Input" : "Output";
        }

        try
        {
            EnumerateMediaTypes(pin, mediaTypes);
            EnumerateVideoCapabilities(pin, videoCapabilities);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        var kind = DeterminePinKind(name, mediaTypes);
        return new DirectShowPinReport(
            name,
            directionName,
            kind,
                GetPinCategory(pin),
            mediaTypes,
                videoCapabilities,
            errorMessage);
    }

    private static void EnumerateMediaTypes(
        IPin pin,
        ICollection<DirectShowMediaTypeReport> destination)
    {
        IEnumMediaTypes? mediaTypeEnumerator = null;

        try
        {
            var hr = pin.EnumMediaTypes(out mediaTypeEnumerator);
            if (hr < 0 || mediaTypeEnumerator is null)
            {
                return;
            }

            var mediaTypes = new AMMediaType[1];
            while (mediaTypeEnumerator.Next(1, mediaTypes, IntPtr.Zero) == 0)
            {
                var mediaType = mediaTypes[0];
                try
                {
                    destination.Add(new DirectShowMediaTypeReport(
                        FriendlyGuid(mediaType.majorType),
                        FriendlyGuid(mediaType.subType),
                        FriendlyGuid(mediaType.formatType),
                        DescribeVideoFormat(mediaType),
                        mediaType.majorType,
                        mediaType.subType,
                        mediaType.formatType));
                }
                finally
                {
                    DsUtils.FreeAMMediaType(mediaType);
                    mediaTypes[0] = null!;
                }
            }
        }
        finally
        {
            ReleaseCom(mediaTypeEnumerator);
        }
    }

    private static void EnumerateVideoCapabilities(
        IPin pin,
        ICollection<DirectShowVideoCapabilityReport> destination)
    {
        if (pin is not IAMStreamConfig streamConfig)
        {
            return;
        }

        IntPtr capabilities = IntPtr.Zero;
        try
        {
            int hr = streamConfig.GetNumberOfCapabilities(out int count, out int capabilitySize);
            if (hr < 0 || count <= 0 || capabilitySize <= 0)
            {
                return;
            }

            capabilities = Marshal.AllocCoTaskMem(capabilitySize);
            for (int index = 0; index < count; index++)
            {
                AMMediaType? mediaType = null;
                try
                {
                    hr = streamConfig.GetStreamCaps(index, out mediaType, capabilities);
                    if (hr >= 0 && mediaType is not null && mediaType.majorType == MediaType.Video)
                    {
                        destination.Add(new DirectShowVideoCapabilityReport(
                            index,
                            DescribeVideoFormat(mediaType)));
                    }
                }
                finally
                {
                    if (mediaType is not null)
                    {
                        DsUtils.FreeAMMediaType(mediaType);
                    }
                }
            }
        }
        finally
        {
            if (capabilities != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(capabilities);
            }
        }
    }

    private static string GetPinCategory(IPin pin)
    {
        if (pin is not IKsPropertySet propertySet)
        {
            return "Not reported";
        }

        IntPtr categoryPointer = IntPtr.Zero;
        try
        {
            categoryPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<Guid>());
            int hr = propertySet.Get(
                PropSetID.Pin,
                (int)AMPropertyPin.Category,
                IntPtr.Zero,
                0,
                categoryPointer,
                Marshal.SizeOf<Guid>(),
                out _);

            if (hr < 0)
            {
                return "Not reported";
            }

            var category = Marshal.PtrToStructure<Guid>(categoryPointer);
            return category == Guid.Parse(PinCategory.Preview.ToString()) ? "Preview"
                : category == Guid.Parse(PinCategory.Capture.ToString()) ? "Capture"
                : category.ToString("D");
        }
        catch
        {
            return "Not reported";
        }
        finally
        {
            if (categoryPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(categoryPointer);
            }
        }
    }

    private static string DescribeVideoFormat(AMMediaType mediaType)
    {
        if (mediaType.majorType != MediaType.Video || mediaType.formatPtr == IntPtr.Zero)
        {
            return string.Empty;
        }

        if (mediaType.formatType == FormatType.VideoInfo)
        {
            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader>(mediaType.formatPtr);
            if (videoInfo.BmiHeader is null)
            {
                return string.Empty;
            }

            return DescribeVideoFormatDetails(
                videoInfo.BmiHeader.Width,
                videoInfo.BmiHeader.Height,
                videoInfo.AvgTimePerFrame,
                "Not reported");
        }

        if (mediaType.formatType == FormatType.VideoInfo2)
        {
            var videoInfo = Marshal.PtrToStructure<VideoInfoHeader2>(mediaType.formatPtr);
            if (videoInfo.BmiHeader is null)
            {
                return string.Empty;
            }

            return DescribeVideoFormatDetails(
                videoInfo.BmiHeader.Width,
                videoInfo.BmiHeader.Height,
                videoInfo.AvgTimePerFrame,
                videoInfo.InterlaceFlags.ToString());
        }

        return string.Empty;
    }

    private static string DescribeVideoFormatDetails(
        int width,
        int height,
        long averageTimePerFrame,
        string interlace)
    {
        double framesPerSecond = averageTimePerFrame > 0
            ? 10_000_000d / averageTimePerFrame
            : 0;

        return $"{width}x{Math.Abs(height)} @ {framesPerSecond:0.###} fps; interlace: {interlace}";
    }

    private static CapturePinKind DeterminePinKind(
        string pinName,
        IEnumerable<DirectShowMediaTypeReport> mediaTypes)
    {
        if (mediaTypes.Any(type => type.MajorType == MediaType.Video))
        {
            return CapturePinKind.Video;
        }

        if (mediaTypes.Any(type => type.MajorType == MediaType.Audio))
        {
            return CapturePinKind.Audio;
        }

        if (pinName.Contains("video", StringComparison.OrdinalIgnoreCase) ||
            pinName.Contains("capture", StringComparison.OrdinalIgnoreCase) ||
            pinName.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            return CapturePinKind.Video;
        }

        if (pinName.Contains("audio", StringComparison.OrdinalIgnoreCase) ||
            pinName.Contains("sound", StringComparison.OrdinalIgnoreCase))
        {
            return CapturePinKind.Audio;
        }

        return CapturePinKind.Other;
    }

    private static string FriendlyGuid(Guid value)
    {
        if (value == MediaType.Video) return "Video";
        if (value == MediaType.Audio) return "Audio";
        if (value == MediaType.Stream) return "Stream";

        if (value == MediaSubType.YUY2) return "YUY2";
        if (value == MediaSubType.UYVY) return "UYVY";
        if (value == MediaSubType.RGB24) return "RGB24";
        if (value == MediaSubType.RGB32) return "RGB32";
        if (value == MediaSubType.MJPG) return "MJPEG";
        if (value == new Guid("00000001-0000-0010-8000-00AA00389B71")) return "PCM";

        if (value == FormatType.VideoInfo) return "VideoInfo";
        if (value == FormatType.VideoInfo2) return "VideoInfo2";
        if (value == FormatType.WaveEx) return "WaveEx";
        if (value == Guid.Empty) return "None";

        return value.ToString("D");
    }

    private static string SafeName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

internal sealed record DirectShowDeviceReport(
    string Name,
    string DevicePath,
    IReadOnlyList<DirectShowPinReport> Pins,
    string? ErrorMessage);

internal sealed record DirectShowPinReport(
    string Name,
    string Direction,
    CapturePinKind Kind,
    string Category,
    IReadOnlyList<DirectShowMediaTypeReport> MediaTypes,
    IReadOnlyList<DirectShowVideoCapabilityReport> VideoCapabilities,
    string? ErrorMessage);

internal sealed record DirectShowMediaTypeReport(
    string MajorTypeName,
    string SubTypeName,
    string FormatTypeName,
    string Details,
    Guid MajorType,
    Guid SubType,
    Guid FormatType);

internal sealed record DirectShowVideoCapabilityReport(int Index, string Description);
