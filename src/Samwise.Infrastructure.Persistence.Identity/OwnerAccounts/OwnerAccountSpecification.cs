using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

public sealed class OwnerAccountSpecification : ISpecification<ApplicationUser, OwnerAccountSummary>
{
    public Task<OwnerAccountSummary?> ApplyAsync(IQueryable<ApplicationUser> queryable, CancellationToken cancellationToken = default) =>
        queryable.AsNoTracking()
            .Select(user => new OwnerAccountSummary(user.Id, user.Email!, user.PasswordHash != null))
            .SingleOrDefaultAsync(cancellationToken);
}
