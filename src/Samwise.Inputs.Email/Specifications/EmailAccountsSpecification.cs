using Marten;
using Samwise.Inputs.Email;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Inputs.Email.Specifications;

public sealed class EmailAccountsSpecification(bool enabledOnly = false)
    : IListSpecification<EmailAccount>
{
    public async Task<IReadOnlyList<EmailAccount>> ApplyAsync(
        IQueryable<EmailAccount> queryable,
        CancellationToken cancellationToken = default)
    {
        if (enabledOnly)
        {
            queryable = queryable.Where(account => account.IsEnabled);
        }

        return await queryable
            .OrderBy(account => account.DisplayName)
            .ToListAsync(cancellationToken);
    }
}
