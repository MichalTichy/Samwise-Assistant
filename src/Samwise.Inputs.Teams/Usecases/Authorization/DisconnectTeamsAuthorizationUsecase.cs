using MediatR;

using Samwise.Inputs.Teams.Authentication;

namespace Samwise.Inputs.Teams.Usecases.Authorization;

public sealed record DisconnectTeamsAuthorizationUsecase : IRequest;

internal sealed class DisconnectTeamsAuthorizationUsecaseHandler(
    ITeamsAuthorizationService authorizationService)
    : IRequestHandler<DisconnectTeamsAuthorizationUsecase>
{
    public Task Handle(DisconnectTeamsAuthorizationUsecase request, CancellationToken cancellationToken) =>
        authorizationService.DisconnectAsync(cancellationToken);
}
