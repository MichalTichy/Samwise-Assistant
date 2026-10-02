using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Notifications;

public sealed class DailyAutoExecutionDigest : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public DateOnly ReportDate { get; set; }
    public DateTimeOffset SentAt { get; set; }

    public static string IdFor(DateOnly reportDate) => $"auto-execution-digest:{reportDate:yyyy-MM-dd}";
}
