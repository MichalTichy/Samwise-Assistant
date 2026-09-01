using Marten;
using Samwise.Domain.Playbooks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Logic.Playbooks.Specifications;

public sealed class PlaybooksSpecification(bool enabledOnly = false) : IListSpecification<Playbook>
{
    public async Task<IReadOnlyList<Playbook>> ApplyAsync(
        IQueryable<Playbook> queryable,
        CancellationToken cancellationToken = default)
    {
        if (enabledOnly)
        {
            queryable = queryable.Where(playbook => playbook.IsEnabled);
        }

        return await queryable
            .OrderBy(playbook => playbook.Name)
            .ToListAsync(cancellationToken);
    }
}
