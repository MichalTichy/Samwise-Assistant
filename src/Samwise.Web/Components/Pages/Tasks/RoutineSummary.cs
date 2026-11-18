namespace Samwise.Web.Components.Pages.Tasks;

public sealed record RoutineSummary(string Id, string Title, bool IsEnabled, DateTimeOffset? NextOccurrenceAt,
    string Cron, string TimeZoneId);
