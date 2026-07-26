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
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string Customer { get; set; } = string.Empty;
    public string TapeLabel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

internal sealed class AppState
{
    public string SaveFolder { get; set; } = string.Empty;
    public string PreferredVideoDevice { get; set; } = string.Empty;
    public string PreferredAudioDevice { get; set; } = string.Empty;
    public List<QueueItem> Queue { get; set; } = [];
    public List<CaptureHistoryItem> History { get; set; } = [];
}
