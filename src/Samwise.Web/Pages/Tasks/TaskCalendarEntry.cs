namespace Samwise.Web.Pages.Tasks;

public sealed record TaskCalendarEntry(string Id, string Title, DateTimeOffset At, string? RoutineId,
    bool IsProjection, bool IsCompleted, int CompletedCount, int ItemCount, string? SourceState = null);
