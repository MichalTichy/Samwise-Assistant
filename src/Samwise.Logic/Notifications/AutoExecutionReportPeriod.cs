namespace Samwise.Logic.Notifications;

public static class AutoExecutionReportPeriod
{
    public static TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static (DateTimeOffset From, DateTimeOffset Until) ForDay(DateOnly date) =>
        (ToUtcMidnight(date), ToUtcMidnight(date.AddDays(1)));

    public static DateTimeOffset ToUtcMidnight(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), TimeZone), TimeSpan.Zero);

    public static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    public static DateTimeOffset LocalTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone);
}
