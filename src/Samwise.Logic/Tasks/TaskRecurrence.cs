using Cronos;

namespace Samwise.Logic.Tasks;

public static class TaskRecurrence
{
    public static DateTimeOffset Next(string cron, string timeZoneId, DateTimeOffset after) =>
        CronExpression.Parse(cron).GetNextOccurrence(after.ToUniversalTime(),
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId))
        ?? throw new ArgumentException(Samwise.Domain.Tasks.TaskErrors.InvalidRecurrence);
}
