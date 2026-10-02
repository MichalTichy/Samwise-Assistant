namespace Samwise.Logic.Notifications;

public static class DailyAutoExecutionSchedule
{
    private static readonly TimeOnly SendAfter = new(8, 0);

    public static DateOnly? ReportDateDue(DateTimeOffset utcNow)
    {
        var localNow = AutoExecutionReportPeriod.LocalTime(utcNow);
        return TimeOnly.FromDateTime(localNow.DateTime) >= SendAfter
            ? DateOnly.FromDateTime(localNow.DateTime).AddDays(-1)
            : null;
    }
}
