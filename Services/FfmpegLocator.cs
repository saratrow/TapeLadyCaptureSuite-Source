using System.Diagnostics;

namespace TapeLadyCaptureSuite.Services;

internal static class FfmpegLocator
{
    public static string? Find()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var pathResult = RunAndCapture("where.exe", "ffmpeg.exe");
        if (!string.IsNullOrWhiteSpace(pathResult))
        {
            var first = pathResult
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                var candidate = Directory
                    .EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Access to one package folder should not stop the search.
            }
        }

        return null;
    }

    public static async Task<int> InstallWithWingetAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget.exe",
            UseShellExecute = true,
            Arguments =
                "install --id Gyan.FFmpeg -e " +
                "--accept-package-agreements --accept-source-agreements"
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Package Manager could not be started.");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static string? RunAndCapture(string fileName, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2500);
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }
}
