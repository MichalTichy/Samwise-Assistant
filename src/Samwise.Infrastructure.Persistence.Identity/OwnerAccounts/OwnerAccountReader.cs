namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

/// <summary>Read-only owner identity used by anonymous sign-in and bootstrap pages.</summary>
public sealed class OwnerAccountReader(ApplicationDbContext database)
{
    public Task<OwnerAccountSummary?> ReadAsync(CancellationToken cancellationToken = default) =>
        new OwnerAccountSpecification().ApplyAsync(database.Users, cancellationToken);
}
