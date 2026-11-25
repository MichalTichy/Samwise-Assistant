namespace Samwise.Domain.Tasks;

public sealed class ExternalTaskReference
{
    public string Provider { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset? DueAt { get; set; }
    public bool IsRecurring { get; set; }
    public int? CompletedCount { get; set; }
    public int Generation { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string ContentFingerprint { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string State { get; set; } = "active";
}
