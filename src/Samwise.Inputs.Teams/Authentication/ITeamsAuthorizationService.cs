namespace Samwise.Inputs.Teams.Authentication;

public interface ITeamsAuthorizationService
{
    Task<TeamsAuthorizationChallenge> StartAsync(CancellationToken cancellationToken = default);
    Task<TeamsConnectionState> GetStateAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
