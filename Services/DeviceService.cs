using DirectShowLib;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services;

internal static class DeviceService
{
    public static IReadOnlyList<CaptureDeviceInfo> GetVideoDevices()
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        var result = new List<CaptureDeviceInfo>();

        for (var index = 0; index < devices.Length; index++)
        {
            var name = string.IsNullOrWhiteSpace(devices[index].Name)
                ? $"Video Device {index + 1}"
                : devices[index].Name;

            result.Add(new CaptureDeviceInfo(index, name));
        }

        return result;
    }

    public static IReadOnlyList<string> GetAudioCaptureDevices()
    {
        var devices = DsDevice.GetDevicesOfCat(FilterCategory.AudioInputDevice);
        var result = new List<string>();

        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.Name))
            {
                result.Add(device.Name);
            }
        }

        return result;
    }
}
