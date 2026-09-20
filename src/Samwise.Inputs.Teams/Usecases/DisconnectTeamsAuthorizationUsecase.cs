using MediatR;

namespace Samwise.Inputs.Teams.Usecases;

public sealed record DisconnectTeamsAuthorizationUsecase : IRequest;

internal sealed class DisconnectTeamsAuthorizationUsecaseHandler(
    ITeamsAuthorizationService authorizationService)
    : IRequestHandler<DisconnectTeamsAuthorizationUsecase>
{
    public Task Handle(DisconnectTeamsAuthorizationUsecase request, CancellationToken cancellationToken) =>
        authorizationService.DisconnectAsync(cancellationToken);
}
