using System.Security.Cryptography;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services;

internal sealed record DeliveryCandidate(string SourcePath, string DestinationFileName, long Length);

internal sealed record DeliveryPlan(
    IReadOnlyList<DeliveryCandidate> Candidates,
    IReadOnlyList<string> BlockingReasons)
{
    public bool CanDeliver => BlockingReasons.Count == 0;
    public long PayloadBytes => Candidates.Sum(candidate => candidate.Length);
}

internal sealed record DeliveryDriveCheck(bool IsValid, string? ErrorMessage, long RequiredBytes, long SafetyMarginBytes);

internal sealed record DeliveryProgress(int CompletedFiles, int TotalFiles, string FileName, string State);

internal static class ProjectDeliveryService
{
    private const long SafetyMarginMinimumBytes = 512L * 1024 * 1024;
    private const long Fat32MaximumFileBytes = (4L * 1024 * 1024 * 1024) - 1;
    private const long Gigabyte = 1024L * 1024 * 1024;

    public static DeliveryPlan BuildPlan(CustomerProject project, IEnumerable<CaptureHistoryItem> historyItems)
    {
        var projectHistory = historyItems.Where(item => item.ProjectId == project.Id).ToList();
        var reasons = new List<string>();
        if (!project.IsCompleted)
        {
            reasons.Add("Delivery is available only after the project is marked complete.");
        }

        var needsReview = projectHistory.Where(item => item.ReviewStatus == CaptureReviewStatus.NeedsReview).ToList();
        if (needsReview.Count > 0)
        {
            int missingFiles = needsReview.Count(item => !File.Exists(item.OutputPath));
            reasons.Add($"Delivery is blocked because {needsReview.Count} recording(s) are still marked Needs Review." +
                (missingFiles > 0 ? $" {missingFiles} of those recording(s) have a missing source file." : string.Empty));
        }

        var candidates = new List<DeliveryCandidate>();
        foreach (CaptureHistoryItem item in projectHistory.Where(IsDeliverableStatus))
        {
            if (!File.Exists(item.OutputPath))
            {
                reasons.Add($"Finished recording is missing: {item.TapeLabel}.");
                continue;
            }

            string destinationFileName = Path.GetFileName(item.OutputPath);
            if (!string.Equals(Path.GetExtension(destinationFileName), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Finished recording is not an MP4: {item.TapeLabel}.");
                continue;
            }

            var sourceInfo = new FileInfo(item.OutputPath);
            if (sourceInfo.Length <= 0)
            {
                reasons.Add($"Finished recording is empty: {item.TapeLabel}.");
                continue;
            }

            candidates.Add(new DeliveryCandidate(item.OutputPath, destinationFileName, sourceInfo.Length));
        }

        if (candidates.Count == 0)
        {
            reasons.Add("There are no finished MP4 recordings available to deliver.");
        }

        foreach (IGrouping<string, DeliveryCandidate> duplicate in candidates.GroupBy(candidate => candidate.DestinationFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (duplicate.Skip(1).Any())
            {
                reasons.Add($"More than one recording would use the USB filename {duplicate.Key}.");
            }
        }

        return new DeliveryPlan(candidates, reasons);
    }

    public static string GetRecommendedUsbCapacity(long payloadBytes)
    {
        long requiredBytes;
        try
        {
            requiredBytes = Math.Max(8 * Gigabyte, checked(payloadBytes + Math.Max(2 * Gigabyte, payloadBytes / 4)));
        }
        catch (OverflowException)
        {
            return "Over 1 TB required";
        }

        long[] capacities = [8, 16, 32, 64, 128, 256, 512, 1024];
        foreach (long capacity in capacities)
        {
            if (requiredBytes <= capacity * Gigabyte)
            {
                return capacity == 1024 ? "1 TB" : $"{capacity} GB";
            }
        }

        return "Over 1 TB required";
    }

    public static string FormatGigabytes(long bytes) => $"{bytes / (double)Gigabyte:0.00} GB";

    public static DeliveryDriveCheck ValidateDrive(RemovableDriveInfo drive, IReadOnlyList<DeliveryCandidate> candidates)
    {
        try
        {
            if (!RemovableDriveService.TryGetTapeLadyDrive(drive.RootPath, out RemovableDriveInfo? currentDrive) || currentDrive is null)
            {
                return new DeliveryDriveCheck(false, "The selected TAPELADY USB drive is no longer available.", 0, 0);
            }

            foreach (DeliveryCandidate candidate in candidates)
            {
                if (string.Equals(currentDrive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase) &&
                    candidate.Length > Fat32MaximumFileBytes)
                {
                    return new DeliveryDriveCheck(false,
                        $"{candidate.DestinationFileName} is too large for the FAT32 USB filesystem. Use a USB filesystem that supports files over 4 GB.",
                        0,
                        0);
                }
            }

            long payload;
            try
            {
                payload = checked(candidates.Sum(candidate => candidate.Length));
            }
            catch (OverflowException)
            {
                return new DeliveryDriveCheck(false, "The delivery size is too large to calculate safely.", 0, 0);
            }

            long margin = Math.Max(SafetyMarginMinimumBytes, (long)Math.Ceiling(payload * 0.02));
            long required;
            try
            {
                required = checked(payload + margin);
            }
            catch (OverflowException)
            {
                return new DeliveryDriveCheck(false, "The delivery size is too large to calculate safely.", 0, 0);
            }

            if (currentDrive.AvailableFreeSpace < required)
            {
                return new DeliveryDriveCheck(false,
                    $"The selected USB has {FormatSize(currentDrive.AvailableFreeSpace)} free, but delivery requires {FormatSize(required)} including the safety margin.",
                    required,
                    margin);
            }

            return new DeliveryDriveCheck(true, null, required, margin);
        }
        catch (IOException ex)
        {
            return new DeliveryDriveCheck(false, ex.Message, 0, 0);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DeliveryDriveCheck(false, ex.Message, 0, 0);
        }
    }

    public static async Task CopyAndVerifyAsync(
        DeliveryCandidate candidate,
        string destinationPath,
        int completedFiles,
        int totalFiles,
        IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        string rootPath = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The USB destination path is invalid.");
        string temporaryPath = Path.Combine(rootPath, $".tlcs-delivery-{Guid.NewGuid():N}.tmp");
        bool temporaryFileCreated = false;

        try
        {
            progress?.Report(new DeliveryProgress(completedFiles, totalFiles, candidate.DestinationFileName, "Copying"));
            byte[] sourceHash;
            long copiedBytes = 0;
            await using (var source = new FileStream(candidate.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                temporaryFileCreated = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    hash.AppendData(buffer, 0, bytesRead);
                    copiedBytes += bytesRead;
                }

                await destination.FlushAsync(cancellationToken);
                sourceHash = hash.GetHashAndReset();
            }

            if (copiedBytes != candidate.Length || new FileInfo(temporaryPath).Length != candidate.Length)
            {
                throw new IOException($"Copied size did not match {candidate.DestinationFileName}.");
            }

            progress?.Report(new DeliveryProgress(completedFiles, totalFiles, candidate.DestinationFileName, "Verifying"));
            byte[] destinationHash;
            await using (var temporaryFile = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                destinationHash = await SHA256.HashDataAsync(temporaryFile, cancellationToken);
            }

            if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
            {
                throw new IOException($"SHA-256 verification failed for {candidate.DestinationFileName}.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            temporaryFileCreated = false;
            progress?.Report(new DeliveryProgress(completedFiles + 1, totalFiles, candidate.DestinationFileName, "Verified"));
        }
        finally
        {
            if (temporaryFileCreated)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public static async Task VerifyExistingAsync(
        DeliveryCandidate candidate,
        string destinationPath,
        int completedFiles,
        int totalFiles,
        IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new DeliveryProgress(completedFiles, totalFiles, candidate.DestinationFileName, "Verifying existing file"));
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length != candidate.Length)
        {
            throw new IOException($"Existing USB file does not match the expected length for {candidate.DestinationFileName}.");
        }

        byte[] sourceHash = await HashFileAsync(candidate.SourcePath, cancellationToken);
        byte[] destinationHash = await HashFileAsync(destinationPath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
        {
            throw new IOException($"Existing USB file does not match {candidate.DestinationFileName}.");
        }

        progress?.Report(new DeliveryProgress(completedFiles + 1, totalFiles, candidate.DestinationFileName, "Verified"));
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static bool IsDeliverableStatus(CaptureHistoryItem item) =>
        item.ReviewStatus is CaptureReviewStatus.CompleteTrimmed or CaptureReviewStatus.CompleteNoTrimNeeded;

    private static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            // A disconnected or inaccessible USB may retain the TLCS temporary file.
        }
    }
}
