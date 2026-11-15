namespace Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

internal static class OwnerAccountConcurrency
{
    // Both legacy startup and interactive registration serialize the single-owner check/create.
    internal const string LockSql = "SELECT pg_advisory_xact_lock(1935764855)";
}
