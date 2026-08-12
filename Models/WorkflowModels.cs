namespace TapeLadyCaptureSuite.Models;

internal sealed class QueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Customer { get; set; } = string.Empty;
    public string TapeLabel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public string? OutputPath { get; set; }
}

internal sealed class CaptureHistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string Customer { get; set; } = string.Empty;
    public string TapeLabel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public CaptureReviewStatus ReviewStatus { get; set; } = CaptureReviewStatus.NeedsReview;
    public string? OriginalBackupPath { get; set; }
    public double? OriginalDurationSeconds { get; set; }
    public double? FinalDurationSeconds { get; set; }
    public double? TrimStartSeconds { get; set; }
    public double? TrimEndSeconds { get; set; }
    public TrimMethod? TrimMethod { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

internal enum CaptureReviewStatus
{
    NeedsReview,
    CompleteTrimmed,
    CompleteNoTrimNeeded
}

internal enum TrimMethod
{
    FastLossless,
    FrameAccurate
}

internal sealed class AppState
{
    public string SaveFolder { get; set; } = string.Empty;
    public string PreferredVideoDevice { get; set; } = string.Empty;
    public string PreferredAudioDevice { get; set; } = string.Empty;
    public List<QueueItem> Queue { get; set; } = [];
    public List<CaptureHistoryItem> History { get; set; } = [];
}
