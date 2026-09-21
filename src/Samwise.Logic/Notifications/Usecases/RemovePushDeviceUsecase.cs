using MediatR;
using Samwise.Domain.Notifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications.Usecases;

public sealed record RemovePushDeviceUsecase(string DeviceId) : IRequest;

internal sealed class RemovePushDeviceUsecaseHandler(IRepository<PushDevice> repository)
    : IRequestHandler<RemovePushDeviceUsecase>
{
    public Task Handle(RemovePushDeviceUsecase request, CancellationToken cancellationToken) =>
        repository.DeleteByIdAsync(request.DeviceId, cancellationToken);
}
