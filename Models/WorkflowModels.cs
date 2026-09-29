namespace TapeLadyCaptureSuite.Models;

internal sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public override string ToString() => FullName;
}

internal sealed class CustomerProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public DateTime DropOffDate { get; set; } = DateTime.Today;
    public string? Name { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? DropOffDate.ToString("MM-dd-yyyy")
        : Name;

    public override string ToString() => DisplayName;
}

internal sealed class QueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CustomerId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Customer { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
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
    public Guid? CustomerId { get; set; }
    public Guid? ProjectId { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string Customer { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
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
    public DateTime? DiscardedAt { get; set; }
}

internal enum CaptureReviewStatus
{
    NeedsReview,
    CompleteTrimmed,
    CompleteNoTrimNeeded,
    Discarded
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
    public List<Customer> Customers { get; set; } = [];
    public List<CustomerProject> Projects { get; set; } = [];
    public List<QueueItem> Queue { get; set; } = [];
    public List<CaptureHistoryItem> History { get; set; } = [];
}
