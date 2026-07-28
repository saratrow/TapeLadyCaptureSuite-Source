using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DirectShowLib;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services;

internal static class DeviceService
{
    public static IReadOnlyList<CaptureDeviceInfo> GetVideoDevices()
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        var result = new List<CaptureDeviceInfo>();

        try
        {
            for (var index = 0; index < devices.Length; index++)
            {
                var name = string.IsNullOrWhiteSpace(devices[index].Name)
                    ? $"Video Device {index + 1}"
                    : devices[index].Name;

                result.Add(new CaptureDeviceInfo(
                    index,
                    name,
                    devices[index].DevicePath ?? string.Empty));
            }
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }

        return result;
    }


    public static IReadOnlyList<string> GetFfmpegAudioCaptureDevices(string ffmpegPath)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return result;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-list_devices");
            startInfo.ArgumentList.Add("true");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("dshow");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("dummy");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return result;
            }

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            foreach (Match match in Regex.Matches(
                         error,
                         "\"(?<name>[^\"]+)\"\\s+\\(audio\\)",
                         RegexOptions.IgnoreCase))
            {
                var name = match.Groups["name"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }
        }
        catch
        {
            // DirectShow enumeration remains available as the fallback.
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<string> GetAudioCaptureDevices()
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);
        var result = new List<string>();

        try
        {
            foreach (var device in devices)
            {
                if (!string.IsNullOrWhiteSpace(device.Name))
                {
                    result.Add(device.Name);
                }
            }
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }

        return result;
    }


    /// <summary>
    /// Returns audio output pins exposed by the selected video capture filter.
    /// Older analog grabbers often expose their red/white RCA audio here
    /// instead of registering a separate Windows recording device.
    /// </summary>
    public static IReadOnlyList<string> GetVideoDeviceAudioPins(
        CaptureDeviceInfo deviceInfo)
    {
        var result = new List<string>();
        DsDevice? selectedDevice = null;
        IBaseFilter? sourceFilter = null;
        IEnumPins? pinEnumerator = null;

        try
        {
            selectedDevice = FindDevice(deviceInfo);
            if (selectedDevice is null)
            {
                return result;
            }

            var filterGuid = typeof(IBaseFilter).GUID;
            selectedDevice.Mon.BindToObject(
                null,
                null,
                ref filterGuid,
                out var sourceObject);

            sourceFilter = (IBaseFilter)sourceObject;
            var hr = sourceFilter.EnumPins(out pinEnumerator);
            DsError.ThrowExceptionForHR(hr);

            var pins = new IPin[1];
            while (pinEnumerator.Next(1, pins, IntPtr.Zero) == 0)
            {
                var pin = pins[0];
                try
                {
                    hr = pin.QueryDirection(out var direction);
                    if (hr < 0 || direction != PinDirection.Output)
                    {
                        continue;
                    }

                    hr = pin.QueryPinInfo(out var pinInfo);
                    if (hr < 0)
                    {
                        continue;
                    }

                    try
                    {
                        var name = pinInfo.name?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(name) &&
                            name.Contains("audio", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(name);
                        }
                    }
                    finally
                    {
                        ReleaseCom(pinInfo.filter);
                    }
                }
                finally
                {
                    ReleaseCom(pin);
                    pins[0] = null!;
                }
            }
        }
        catch
        {
            // Some drivers do not permit pin inspection until a graph is run.
            // In that case the normal Windows audio-device list remains usable.
        }
        finally
        {
            ReleaseCom(pinEnumerator);
            ReleaseCom(sourceFilter);
            selectedDevice?.Dispose();
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Routes an analog capture device to Composite/RCA or S-Video when the
    /// driver exposes a DirectShow crossbar. Devices without a crossbar are
    /// left unchanged and return false.
    /// </summary>
    public static bool TrySetVideoInput(
        CaptureDeviceInfo deviceInfo,
        VideoInputKind inputKind,
        out string message)
    {
        IFilterGraph2? graph = null;
        ICaptureGraphBuilder2? builder = null;
        IBaseFilter? sourceFilter = null;
        object? crossbarObject = null;
        DsDevice? selectedDevice = null;

        try
        {
            selectedDevice = FindDevice(deviceInfo);
            if (selectedDevice is null)
            {
                message = "The selected capture device could not be found again.";
                return false;
            }

            graph = (IFilterGraph2)new FilterGraph();
            builder = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();

            var hr = builder.SetFiltergraph(graph);
            DsError.ThrowExceptionForHR(hr);

            var filterGuid = typeof(IBaseFilter).GUID;
            selectedDevice.Mon.BindToObject(
                null,
                null,
                ref filterGuid,
                out var sourceObject);

            sourceFilter = (IBaseFilter)sourceObject;
            hr = graph.AddFilter(sourceFilter, selectedDevice.Name);
            DsError.ThrowExceptionForHR(hr);

            var crossbarGuid = typeof(IAMCrossbar).GUID;
            hr = builder.FindInterface(
                FindDirection.UpstreamOnly,
                null,
                sourceFilter,
                crossbarGuid,
                out crossbarObject);

            if (hr < 0 || crossbarObject is not IAMCrossbar crossbar)
            {
                message = "This device does not expose a Composite/S-Video selector.";
                return false;
            }

            hr = crossbar.get_PinCounts(out var outputCount, out var inputCount);
            DsError.ThrowExceptionForHR(hr);

            var wantedConnector = inputKind == VideoInputKind.SVideo
                ? PhysicalConnectorType.Video_SVideo
                : PhysicalConnectorType.Video_Composite;

            for (var output = 0; output < outputCount; output++)
            {
                for (var input = 0; input < inputCount; input++)
                {
                    crossbar.get_CrossbarPinInfo(
                        true,
                        input,
                        out _,
                        out var inputType);

                    if (inputType != wantedConnector || crossbar.CanRoute(output, input) != 0)
                    {
                        continue;
                    }

                    hr = crossbar.Route(output, input);
                    DsError.ThrowExceptionForHR(hr);

                    message = inputKind == VideoInputKind.SVideo
                        ? "S-Video input selected."
                        : "Composite / RCA input selected.";
                    return true;
                }
            }

            message = inputKind == VideoInputKind.SVideo
                ? "The driver did not report an S-Video input."
                : "The driver did not report a Composite / RCA input.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Unable to change the video input: {ex.Message}";
            return false;
        }
        finally
        {
            selectedDevice?.Dispose();
            ReleaseCom(crossbarObject);
            ReleaseCom(sourceFilter);
            ReleaseCom(builder);
            ReleaseCom(graph);
        }
    }

    private static DsDevice? FindDevice(CaptureDeviceInfo requested)
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

        foreach (var device in devices)
        {
            var pathMatches = !string.IsNullOrWhiteSpace(requested.DevicePath) &&
                              string.Equals(
                                  device.DevicePath,
                                  requested.DevicePath,
                                  StringComparison.OrdinalIgnoreCase);

            var nameMatches = string.Equals(
                device.Name,
                requested.Name,
                StringComparison.OrdinalIgnoreCase);

            if (pathMatches || nameMatches)
            {
                foreach (var other in devices)
                {
                    if (!ReferenceEquals(other, device))
                    {
                        other.Dispose();
                    }
                }

                return device;
            }
        }

        foreach (var device in devices)
        {
            device.Dispose();
        }

        return null;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
