using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inputs.Email;
using Samwise.Logic.Inputs.Email.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Inputs.Email;

public sealed class EmailAccountUsecaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewAccountStartsAtCurrentInboxCheckpoint()
    {
        var repository = new EmailAccountRepository();
        var reader = new StubEmailInboxReader(
            new EmailInboxCheckpoint(42, 173, Now));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(CreateRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, reader.CheckpointReadCount);
        var account = Assert.IsType<EmailAccount>(repository.Account);
        Assert.Equal(42u, account.InboxCheckpoint?.UidValidity);
        Assert.Equal(173u, account.InboxCheckpoint?.NextUid);
        Assert.Equal("protected:secret", account.ProtectedPassword);
    }

    [Fact]
    public async Task EditingAccountMetadataPreservesItsInboxCheckpoint()
    {
        var originalCheckpoint = new EmailInboxCheckpoint(7, 99, Now.AddDays(-1));
        var existing = new EmailAccount
        {
            Id = "personal",
            DisplayName = "Old name",
            EmailAddress = "sam@example.com",
            Host = "imap.example.com",
            Port = 993,
            Security = EmailConnectionSecurity.SslOnConnect,
            Username = "sam@example.com",
            ProtectedPassword = "protected:secret",
            InboxCheckpoint = originalCheckpoint
        };
        var repository = new EmailAccountRepository(existing);
        var reader = new StubEmailInboxReader(new EmailInboxCheckpoint(42, 173, Now));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            CreateRequest() with
            {
                EmailAccountId = existing.Id,
                DisplayName = "Personal mail",
                Password = null
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, reader.CheckpointReadCount);
        Assert.Same(originalCheckpoint, repository.Account?.InboxCheckpoint);
        Assert.Equal("Personal mail", repository.Account?.DisplayName);
    }

    private static SaveEmailAccountUsecase CreateRequest() => new(
        null,
        "Personal",
        "sam@example.com",
        "imap.example.com",
        993,
        EmailConnectionSecurity.SslOnConnect,
        "sam@example.com",
        "secret",
        true);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubCredentialProtector : IEmailCredentialProtector
    {
        public string Protect(string password) => $"protected:{password}";

        public string Unprotect(string protectedPassword) =>
            protectedPassword.Replace("protected:", string.Empty, StringComparison.Ordinal);
    }

    private sealed class StubEmailInboxReader(EmailInboxCheckpoint checkpoint) : IEmailInboxReader
    {
        public int CheckpointReadCount { get; private set; }

        public Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<EmailInboxCheckpoint> GetCurrentInboxCheckpointAsync(
            EmailAccount account,
            CancellationToken cancellationToken)
        {
            CheckpointReadCount++;
            return Task.FromResult(checkpoint);
        }

        public Task<NewEmailReadResult> ReadNewAsync(
            EmailAccount account,
            IReadOnlyCollection<string> ignoredSenderAddresses,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailMessageSnapshot>> ReadUnreadAsync(
            EmailAccount account,
            IReadOnlyCollection<string> ignoredSenderAddresses,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmailAccountRepository(EmailAccount? account = null) : IRepository<EmailAccount>
    {
        public EmailAccount? Account { get; private set; } = account;

        public Task<EmailAccount> AddAsync(
            EmailAccount entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            Account = entity;
            return Task.FromResult(entity);
        }

        public Task<EmailAccount?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(Account?.Id == id ? Account : null);

        public Task UpdateAsync(EmailAccount entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            Account = entity;
            return Task.CompletedTask;
        }

        public Task AddAsync(ICollection<EmailAccount> entities, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailAccount>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<EmailAccount?> GetBySpecAsync(ISpecification<EmailAccount> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<EmailAccount, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailAccount>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailAccount>> ListAsync(IListSpecification<EmailAccount> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<EmailAccount, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task GetAndUpdateAsync(string id, Func<EmailAccount, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteAsync(EmailAccount entity, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}
