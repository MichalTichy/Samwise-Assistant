using Shared.Infrastructure.Persistence;

namespace Samwise.Inputs.Teams.Configuration;

public sealed class TeamsInputConfiguration : IItemWithId
{
    public const string DocumentId = "teams-input";
    public const int DefaultPollingIntervalMinutes = 5;
    public const int DefaultDebounceMinutes = 2;

    public string Id { get; set; } = DocumentId;
    public TeamsGraphApplicationConfiguration MicrosoftGraph { get; set; } = new();
    public int PollingIntervalMinutes { get; set; } = DefaultPollingIntervalMinutes;
    public int DebounceMinutes { get; set; } = DefaultDebounceMinutes;
    public string IntakeGuidance { get; set; } = string.Empty;
    public bool IncludeChatMessages { get; set; } = true;
    public bool IncludeChannelMessages { get; set; } = true;
    public bool IncludeCallTranscripts { get; set; }
    public List<TeamsInputSource> IgnoredPeople { get; set; } = [];
    public List<TeamsInputSource> IgnoredChats { get; set; } = [];
    public List<TeamsInputSource> IgnoredChannels { get; set; } = [];
    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
    public string? LastError { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MicrosoftGraph.TenantId)
        && !string.IsNullOrWhiteSpace(MicrosoftGraph.ClientId);

    public void Update(
        string tenantId,
        string clientId,
        string? protectedClientSecret,
        int pollingIntervalMinutes,
        int debounceMinutes,
        string? intakeGuidance,
        bool includeChatMessages,
        bool includeChannelMessages,
        bool includeCallTranscripts,
        IEnumerable<TeamsInputSource> ignoredPeople,
        IEnumerable<TeamsInputSource> ignoredChats,
        IEnumerable<TeamsInputSource> ignoredChannels)
    {
        if (pollingIntervalMinutes is < 1 or > 1_440)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingIntervalMinutes),
                "The polling interval must be between 1 and 1,440 minutes.");
        }

        if (debounceMinutes is < 0 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounceMinutes),
                "The debounce interval must be between 0 and 60 minutes.");
        }

        MicrosoftGraph = new TeamsGraphApplicationConfiguration
        {
            TenantId = tenantId.Trim(),
            ClientId = clientId.Trim(),
            ProtectedClientSecret = protectedClientSecret
        };
        PollingIntervalMinutes = pollingIntervalMinutes;
        DebounceMinutes = debounceMinutes;
        IntakeGuidance = intakeGuidance?.Trim() ?? string.Empty;
        IncludeChatMessages = includeChatMessages;
        IncludeChannelMessages = includeChannelMessages;
        IncludeCallTranscripts = includeCallTranscripts;
        IgnoredPeople = Normalize(ignoredPeople);
        IgnoredChats = Normalize(ignoredChats);
        IgnoredChannels = Normalize(ignoredChannels);
    }

    public void Update(
        int pollingIntervalMinutes,
        int debounceMinutes,
        string? intakeGuidance,
        bool includeChatMessages,
        bool includeChannelMessages,
        bool includeCallTranscripts,
        IEnumerable<TeamsInputSource> ignoredPeople,
        IEnumerable<TeamsInputSource> ignoredChats,
        IEnumerable<TeamsInputSource> ignoredChannels) => Update(
            MicrosoftGraph.TenantId,
            MicrosoftGraph.ClientId,
            MicrosoftGraph.ProtectedClientSecret,
            pollingIntervalMinutes,
            debounceMinutes,
            intakeGuidance,
            includeChatMessages,
            includeChannelMessages,
            includeCallTranscripts,
            ignoredPeople,
            ignoredChats,
            ignoredChannels);

    public bool IsPersonIgnored(string? id) => Contains(IgnoredPeople, id);

    public bool IsChatIgnored(string? id) => Contains(IgnoredChats, id);

    public bool IsChannelIgnored(string? id) => Contains(IgnoredChannels, id);

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

    public void RecordDeferredCheck() => LastError = null;

    private static bool Contains(IEnumerable<TeamsInputSource> sources, string? id) =>
        !string.IsNullOrWhiteSpace(id) && sources.Any(source =>
            string.Equals(source.Id, id.Trim(), StringComparison.Ordinal));

    private static List<TeamsInputSource> Normalize(IEnumerable<TeamsInputSource> sources) => sources
        .Where(source => !string.IsNullOrWhiteSpace(source.Id))
        .Select(source => new TeamsInputSource
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
