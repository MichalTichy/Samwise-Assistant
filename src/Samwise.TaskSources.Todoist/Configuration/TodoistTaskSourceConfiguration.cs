using Shared.Infrastructure.Persistence;

namespace Samwise.TaskSources.Todoist.Configuration;

public sealed class TodoistTaskSourceConfiguration : IItemWithId
{
    // Persisted identity from the former input module; changing it would disconnect saved settings.
    public const string DocumentId = "todoist-input";
    public const int DefaultPollingIntervalMinutes = 15;
    public const int DefaultLeadHours = 24;
    public const int DefaultOverdueDays = 7;

    public string Id { get; set; } = DocumentId;
    public string? ProtectedApiToken { get; set; }
    public int PollingIntervalMinutes { get; set; } = DefaultPollingIntervalMinutes;
    public int LeadHours { get; set; } = DefaultLeadHours;
    // Retained for compatibility with saved settings; dated task import has no age cutoff.
    public int OverdueDays { get; set; } = DefaultOverdueDays;
    public string IntakeGuidance { get; set; } = string.Empty;
    public List<TodoistSourceSelection> IgnoredProjects { get; set; } = [];
    public List<TodoistSourceSelection> IgnoredLabels { get; set; } = [];
    public List<TodoistSourceSelection> IncludedProjects { get; set; } = [];
    public List<TodoistSourceSelection> IncludedLabels { get; set; } = [];
    // Absent in documents saved before task removal existed; deserialization then keeps this empty default.
    public List<TodoistDismissedTask> DismissedTasks { get; set; } = [];
    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
    public string? LastError { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProtectedApiToken);

    public void Update(
        string? protectedApiToken,
        int pollingIntervalMinutes,
        int leadHours,
        int overdueDays,
        string? intakeGuidance,
        IEnumerable<TodoistSourceSelection> ignoredProjects,
        IEnumerable<TodoistSourceSelection> ignoredLabels,
        IEnumerable<TodoistSourceSelection>? includedProjects = null,
        IEnumerable<TodoistSourceSelection>? includedLabels = null)
    {
        if (pollingIntervalMinutes is < 1 or > 1_440)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingIntervalMinutes),
                "The polling interval must be between 1 and 1,440 minutes.");
        }

        if (leadHours is < 0 or > 720)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadHours),
                "The lead time must be between 0 and 720 hours.");
        }

        if (overdueDays is < 0 or > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overdueDays),
                "The overdue range must be between 0 and 365 days.");
        }

        ProtectedApiToken = string.IsNullOrWhiteSpace(protectedApiToken) ? null : protectedApiToken;
        PollingIntervalMinutes = pollingIntervalMinutes;
        LeadHours = leadHours;
        OverdueDays = overdueDays;
        IntakeGuidance = intakeGuidance?.Trim() ?? string.Empty;
        IgnoredProjects = Normalize(ignoredProjects);
        IgnoredLabels = Normalize(ignoredLabels);
        if (includedProjects is not null) IncludedProjects = Normalize(includedProjects);
        if (includedLabels is not null) IncludedLabels = Normalize(includedLabels);
    }

    // Legacy inclusion lists are retained for configuration round-tripping, but no longer gate import.
    public bool IsSelected(string projectId, IReadOnlyList<string> labels) =>
        !IsProjectIgnored(projectId) && !labels.Any(IsLabelIgnored);

    public bool IsProjectIgnored(string? projectId) => Contains(IgnoredProjects, projectId);

    public bool IsLabelIgnored(string? label) => Contains(IgnoredLabels, label);

    public bool IsTaskDismissed(string? taskId) =>
        !string.IsNullOrWhiteSpace(taskId) && (DismissedTasks ?? []).Any(task =>
            string.Equals(task.TaskId, taskId.Trim(), StringComparison.Ordinal));

    /// <summary>Excludes the Todoist task and all its future occurrences; repeating it keeps the original removal time.</summary>
    public void DismissTask(string taskId, string title, string url, string? agentTaskId, DateTimeOffset dismissedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        DismissedTasks ??= [];
        var id = taskId.Trim();
        var existing = DismissedTasks.FirstOrDefault(task => string.Equals(task.TaskId, id, StringComparison.Ordinal));
        if (existing is null)
        {
            existing = new TodoistDismissedTask { TaskId = id, DismissedAt = dismissedAt };
            DismissedTasks.Add(existing);
        }

        existing.Title = string.IsNullOrWhiteSpace(title) ? id : title.Trim();
        existing.Url = url?.Trim() ?? string.Empty;
        existing.AgentTaskId = agentTaskId;
    }

    public bool RestoreTask(string taskId) =>
        !string.IsNullOrWhiteSpace(taskId) && (DismissedTasks ?? []).RemoveAll(task =>
            string.Equals(task.TaskId, taskId.Trim(), StringComparison.Ordinal)) > 0;

    public bool IsWithinWindow(DateTimeOffset dueAt, DateTimeOffset now) =>
        dueAt <= now.AddHours(LeadHours) && dueAt >= now.AddDays(-OverdueDays);

    public void RecordSuccessfulCheck(DateTimeOffset checkedAt)
    {
        LastSuccessfulCheckAt = checkedAt;
        LastError = null;
    }

    public void RecordFailedCheck(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        LastError = error.Trim();
    }

    private static bool Contains(IEnumerable<TodoistSourceSelection> sources, string? id) =>
        !string.IsNullOrWhiteSpace(id) && sources.Any(source =>
            string.Equals(source.Id, id.Trim(), StringComparison.Ordinal));

    private static List<TodoistSourceSelection> Normalize(IEnumerable<TodoistSourceSelection> sources) => sources
        .Where(source => !string.IsNullOrWhiteSpace(source.Id))
        .Select(source => new TodoistSourceSelection
        {
            Id = source.Id.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? source.Id.Trim()
                : source.DisplayName.Trim()
        })
        .GroupBy(source => source.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(source => source.Id, StringComparer.Ordinal)
        .ToList();
}
