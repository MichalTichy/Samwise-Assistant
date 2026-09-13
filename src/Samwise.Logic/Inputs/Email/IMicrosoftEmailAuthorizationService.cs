namespace Samwise.Logic.Inputs.Email;

public interface IMicrosoftEmailAuthorizationService
{
    Task<MicrosoftEmailAuthorizationChallenge> StartAsync(CancellationToken cancellationToken);

    Task<MicrosoftEmailAuthorizationCompletion> CompleteAsync(
        string authorizationId,
        CancellationToken cancellationToken);

    void Finish(string authorizationId);
}
