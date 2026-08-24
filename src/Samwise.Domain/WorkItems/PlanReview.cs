namespace Samwise.Domain.WorkItems;

public enum PlanReviewKind
{
    Approved,
    Rejected
}

public sealed class PlanReview
{
    public PlanReviewKind Kind { get; set; }
    public string? Feedback { get; set; }
    public string Source { get; set; } = "Web";
    public DateTimeOffset ReviewedAt { get; set; }
}
