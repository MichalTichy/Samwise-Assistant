using Marten;
using Samwise.Domain.Notifications;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Notifications.Specifications;

public sealed class PushDevicesSpecification : IListSpecification<PushDevice>
{
    public async Task<IReadOnlyList<PushDevice>> ApplyAsync(
        IQueryable<PushDevice> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .OrderBy(device => device.CreatedAt)
            .ToListAsync(cancellationToken);
}
