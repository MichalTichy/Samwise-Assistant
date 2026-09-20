using MediatR;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Configuration;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Teams.Usecases.Configuration;

public sealed record SaveTeamsInputConfigurationUsecase(
    string TenantId,
    string ClientId,
    string? ClientSecret,
    bool RemoveClientSecret,
    int PollingIntervalMinutes,
    int DebounceMinutes,
    string? IntakeGuidance,
    bool IncludeChatMessages,
    bool IncludeChannelMessages,
    bool IncludeCallTranscripts,
    IReadOnlyList<TeamsInputSource> IgnoredPeople,
    IReadOnlyList<TeamsInputSource> IgnoredChats,
    IReadOnlyList<TeamsInputSource> IgnoredChannels)
    : IRequest<TeamsInputConfigurationSaveResult>;

internal sealed class SaveTeamsInputConfigurationUsecaseHandler(
    IRepository<TeamsInputConfiguration> repository,
    ITeamsCredentialProtector credentialProtector)
    : IRequestHandler<SaveTeamsInputConfigurationUsecase, TeamsInputConfigurationSaveResult>
{
    public async Task<TeamsInputConfigurationSaveResult> Handle(
        SaveTeamsInputConfigurationUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new TeamsInputConfigurationSaveResult(false, errors);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            TeamsInputConfiguration.DocumentId,
            cancellationToken);
        var protectedClientSecret = request.RemoveClientSecret
            ? null
            : string.IsNullOrWhiteSpace(request.ClientSecret)
                ? configuration.MicrosoftGraph.ProtectedClientSecret
                : credentialProtector.Protect(request.ClientSecret.Trim());
        configuration.Update(
            request.TenantId,
            request.ClientId,
            protectedClientSecret,
            request.PollingIntervalMinutes,
            request.DebounceMinutes,
            request.IntakeGuidance,
            request.IncludeChatMessages,
            request.IncludeChannelMessages,
            request.IncludeCallTranscripts,
            request.IgnoredPeople,
            request.IgnoredChats,
            request.IgnoredChannels);
        await repository.UpdateAsync(configuration, cancellationToken);
        return new TeamsInputConfigurationSaveResult(true, []);
    }

    private static List<string> Validate(SaveTeamsInputConfigurationUsecase request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            errors.Add("The Microsoft tenant ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            errors.Add("The Microsoft application client ID is required.");
        }

        if (request.PollingIntervalMinutes is < 1 or > 1_440)
        {
            errors.Add("The polling interval must be between 1 and 1,440 minutes.");
        }

        if (request.DebounceMinutes is < 0 or > 60)
        {
            errors.Add("The debounce interval must be between 0 and 60 minutes.");
        }

        ValidateSources(request.IgnoredPeople, "person", errors);
        ValidateSources(request.IgnoredChats, "chat", errors);
        ValidateSources(request.IgnoredChannels, "channel", errors);
        return errors;
    }

    private static void ValidateSources(
        IEnumerable<TeamsInputSource> sources,
        string kind,
        ICollection<string> errors)
    {
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(source.DisplayName))
            {
                errors.Add($"Every ignored Teams {kind} must have an ID and display name.");
                return;
            }
        }
    }
}
