namespace Samwise.Web.Components.Pages.Tasks;

public sealed record TaskCalendarEntry(string Id, string Title, DateTimeOffset At, string? RoutineId,
    bool IsProjection, bool IsCompleted, int CompletedCount, int ItemCount, string? SourceState = null);
