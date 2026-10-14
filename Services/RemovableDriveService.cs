using System.IO;

namespace TapeLadyCaptureSuite.Services;

internal sealed record RemovableDriveInfo(
    string RootPath,
    string VolumeLabel,
    string DriveFormat,
    long AvailableFreeSpace)
{
    public override string ToString() => $"{RootPath} ({VolumeLabel})";
}

internal static class RemovableDriveService
{
    public const string TapeLadyVolumeLabel = "TAPELADY";

    public static IReadOnlyList<RemovableDriveInfo> FindTapeLadyDrives()
    {
        var drives = new List<RemovableDriveInfo>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Removable ||
                    !string.Equals(drive.VolumeLabel, TapeLadyVolumeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                drives.Add(new RemovableDriveInfo(
                    drive.RootDirectory.FullName,
                    drive.VolumeLabel,
                    drive.DriveFormat,
                    drive.AvailableFreeSpace));
            }
            catch (IOException)
            {
                // Drives can disappear while Windows enumerates them.
            }
            catch (UnauthorizedAccessException)
            {
                // An inaccessible drive cannot be used for delivery.
            }
        }

        return drives.OrderBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool TryGetTapeLadyDrive(string rootPath, out RemovableDriveInfo? driveInfo)
    {
        driveInfo = FindTapeLadyDrives().FirstOrDefault(drive =>
            string.Equals(drive.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));
        return driveInfo is not null;
    }
}
