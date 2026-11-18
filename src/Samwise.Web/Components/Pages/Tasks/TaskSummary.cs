namespace Samwise.Web.Components.Pages.Tasks;

public sealed record TaskSummary(string Id, string Title, DateTimeOffset? NextRunAt,
    DateTimeOffset? CompletedAt, int ItemCount, int CompletedCount, DateTimeOffset? LastRunAt,
    string? RoutineId, DateTimeOffset? OccurrenceAt, string? SourceState = null);
