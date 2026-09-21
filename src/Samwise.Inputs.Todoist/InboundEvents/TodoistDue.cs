using System.Globalization;

namespace Samwise.Inputs.Todoist.InboundEvents;

/// <summary>
/// Todoist due value. <see cref="Date"/> is either an all-day date (<c>2026-09-21</c>), a floating
/// local date-time (<c>2026-09-21T15:00:00</c>), or a fixed UTC/offset date-time.
/// </summary>
public sealed class TodoistDue
{
    public string Date { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Text { get; set; }
    public bool IsRecurring { get; set; }

    public bool IsAllDay => Date.Trim().Length == 10;

    /// <summary>
    /// Resolves the due instant. All-day tasks are due at the start of their day. Floating values use the
    /// task timezone, then <paramref name="fallbackTimeZone"/> (the Todoist account timezone), then UTC.
    /// </summary>
    public DateTimeOffset? ResolveDueAt(string? fallbackTimeZone)
    {
        var value = Date.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return null;
        }

        if (!IsAllDay && parsed.Kind != DateTimeKind.Unspecified)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }

        var timeZone = FindTimeZone(Timezone) ?? FindTimeZone(fallbackTimeZone) ?? TimeZoneInfo.Utc;
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static TimeZoneInfo? FindTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var timeZone)
            ? timeZone
            : null;
}
