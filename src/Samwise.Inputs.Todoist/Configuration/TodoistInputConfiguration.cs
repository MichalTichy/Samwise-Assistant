using Shared.Infrastructure.Persistence;

namespace Samwise.Inputs.Todoist.Configuration;

public sealed class TodoistInputConfiguration : IItemWithId
{
    public const string DocumentId = "todoist-input";
    public const int DefaultPollingIntervalMinutes = 15;
    public const int DefaultLeadHours = 24;
    public const int DefaultOverdueDays = 7;

    public string Id { get; set; } = DocumentId;
    public string? ProtectedApiToken { get; set; }
    public int PollingIntervalMinutes { get; set; } = DefaultPollingIntervalMinutes;
    public int LeadHours { get; set; } = DefaultLeadHours;
    public int OverdueDays { get; set; } = DefaultOverdueDays;
    public string IntakeGuidance { get; set; } = string.Empty;
    public List<TodoistInputSource> IgnoredProjects { get; set; } = [];
    public List<TodoistInputSource> IgnoredLabels { get; set; } = [];
    public List<TodoistInputSource> IncludedProjects { get; set; } = [];
    public List<TodoistInputSource> IncludedLabels { get; set; } = [];
    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
    public string? LastError { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProtectedApiToken);

    public void Update(
        string? protectedApiToken,
        int pollingIntervalMinutes,
        int leadHours,
        int overdueDays,
        string? intakeGuidance,
        IEnumerable<TodoistInputSource> ignoredProjects,
        IEnumerable<TodoistInputSource> ignoredLabels,
        IEnumerable<TodoistInputSource>? includedProjects = null,
        IEnumerable<TodoistInputSource>? includedLabels = null)
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

    public bool IsSelected(string projectId, IReadOnlyList<string> labels) =>
        !IsProjectIgnored(projectId) && !labels.Any(IsLabelIgnored) &&
        (Contains(IncludedProjects, projectId) || labels.Any(label => Contains(IncludedLabels, label)));

    public bool IsProjectIgnored(string? projectId) => Contains(IgnoredProjects, projectId);

    public bool IsLabelIgnored(string? label) => Contains(IgnoredLabels, label);

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

    private static bool Contains(IEnumerable<TodoistInputSource> sources, string? id) =>
        !string.IsNullOrWhiteSpace(id) && sources.Any(source =>
            string.Equals(source.Id, id.Trim(), StringComparison.Ordinal));

    private static List<TodoistInputSource> Normalize(IEnumerable<TodoistInputSource> sources) => sources
        .Where(source => !string.IsNullOrWhiteSpace(source.Id))
        .Select(source => new TodoistInputSource
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
