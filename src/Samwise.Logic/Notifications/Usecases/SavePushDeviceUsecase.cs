using MediatR;
using Samwise.Domain.Notifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Notifications.Usecases;

public sealed record SavePushDeviceUsecase(
    string Endpoint,
    string P256dh,
    string Auth,
    string Name) : IRequest<string>;

internal sealed class SavePushDeviceUsecaseHandler(
    IRepository<PushDevice> repository,
    TimeProvider timeProvider) : IRequestHandler<SavePushDeviceUsecase, string>
{
    public async Task<string> Handle(SavePushDeviceUsecase request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A push subscription endpoint must be an absolute HTTPS URL.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.P256dh);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Auth);
        var id = PushDevice.CreateId(request.Endpoint);
        var name = string.IsNullOrWhiteSpace(request.Name) ? "This device" : request.Name.Trim();
        var existing = await repository.GetByIdAsync(id, cancellationToken);
        if (existing is not null)
        {
            existing.Name = name;
            existing.P256dh = request.P256dh.Trim();
            existing.Auth = request.Auth.Trim();
            await repository.UpdateAsync(existing, cancellationToken);
            return id;
        }

        await repository.AddAsync(new PushDevice
        {
            Id = id,
            Name = name,
            Endpoint = request.Endpoint.Trim(),
            P256dh = request.P256dh.Trim(),
            Auth = request.Auth.Trim(),
            CreatedAt = timeProvider.GetUtcNow()
        }, cancellationToken);
        return id;
    }
}
