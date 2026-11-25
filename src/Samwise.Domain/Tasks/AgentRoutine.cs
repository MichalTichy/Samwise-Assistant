using JasperFx;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Tasks;

public sealed class AgentRoutine : IItemWithId, IRevisioned
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public List<TaskChecklistItem> Checklist { get; set; } = [];
    public string Cron { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Europe/Prague";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? NextOccurrenceAt { get; set; }

    public AgentTask CreateOccurrence()
    {
        var occurrence = NextOccurrenceAt ?? throw new InvalidOperationException(Samwise.Domain.Tasks.TaskErrors.NoOccurrence);
        var task = new AgentTask
        {
            Id = $"{Id}:{occurrence.UtcTicks}", RoutineId = Id, OccurrenceAt = occurrence
        };
        task.Update(Title, Instructions, Checklist.Select(item => item.Copy(reset: true)), occurrence, occurrence);
        return task;
    }
}
