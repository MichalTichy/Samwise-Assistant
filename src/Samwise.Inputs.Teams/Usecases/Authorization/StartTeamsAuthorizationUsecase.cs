using MediatR;

using Samwise.Inputs.Teams.Authentication;

namespace Samwise.Inputs.Teams.Usecases.Authorization;

public sealed record StartTeamsAuthorizationUsecase : IRequest<TeamsAuthorizationOperationResult>;

internal sealed class StartTeamsAuthorizationUsecaseHandler(
    ITeamsAuthorizationService authorizationService)
    : IRequestHandler<StartTeamsAuthorizationUsecase, TeamsAuthorizationOperationResult>
{
    public async Task<TeamsAuthorizationOperationResult> Handle(
        StartTeamsAuthorizationUsecase request,
        CancellationToken cancellationToken)
    {
        try
        {
            var challenge = await authorizationService.StartAsync(cancellationToken);
            return new TeamsAuthorizationOperationResult(true, challenge, []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TeamsAuthorizationOperationResult(false, null, [exception.Message]);
        }
    }
}
