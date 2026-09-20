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
            new EmailMailboxCheckpoint(42, 173, Now));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            new StubMicrosoftEmailAuthorizationService(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(CreateRequest(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, reader.InboxCheckpointReadCount);
        Assert.Equal(1, reader.SentCheckpointReadCount);
        var account = Assert.IsType<EmailAccount>(repository.Account);
        Assert.Equal(42u, account.InboxCheckpoint?.UidValidity);
        Assert.Equal(173u, account.InboxCheckpoint?.NextUid);
        Assert.Equal("protected:secret", account.ProtectedPassword);
        Assert.Equal("Personal mailbox guidance.", account.IntakeGuidance);
    }

    [Fact]
    public async Task EditingAccountMetadataPreservesItsInboxCheckpoint()
    {
        var originalCheckpoint = new EmailMailboxCheckpoint(7, 99, Now.AddDays(-1));
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
            InboxCheckpoint = originalCheckpoint,
            SentCheckpoint = originalCheckpoint
        };
        var repository = new EmailAccountRepository(existing);
        var reader = new StubEmailInboxReader(new EmailMailboxCheckpoint(42, 173, Now));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            new StubMicrosoftEmailAuthorizationService(),
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
        Assert.Equal(0, reader.InboxCheckpointReadCount);
        Assert.Equal(0, reader.SentCheckpointReadCount);
        Assert.Same(originalCheckpoint, repository.Account?.InboxCheckpoint);
        Assert.Equal("Personal mail", repository.Account?.DisplayName);
    }

    [Fact]
    public async Task NewMicrosoft365AccountStoresAuthorizedSessionAndInitializesCheckpoint()
    {
        var repository = new EmailAccountRepository();
        var reader = new StubEmailInboxReader(new EmailMailboxCheckpoint(42, 173, Now));
        var authorization = new StubMicrosoftEmailAuthorizationService(
            new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Succeeded,
                "serialized-cache",
                "home-account",
                "sam@work.example",
                null));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            authorization,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            CreateRequest() with
            {
                AuthenticationMethod = EmailAuthenticationMethod.Microsoft365OAuth,
                Username = "sam@work.example",
                Password = null,
                MicrosoftAuthorizationId = "authorization"
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var account = Assert.IsType<EmailAccount>(repository.Account);
        Assert.Equal(EmailAuthenticationMethod.Microsoft365OAuth, account.AuthenticationMethod);
        Assert.Equal("protected:serialized-cache", account.ProtectedOAuthTokenCache);
        Assert.Equal("home-account", account.OAuthHomeAccountId);
        Assert.Empty(account.ProtectedPassword);
        Assert.Equal(1, reader.InboxCheckpointReadCount);
        Assert.Equal(1, reader.SentCheckpointReadCount);
    }

    [Fact]
    public async Task PendingMicrosoft365SignInDoesNotSaveAccount()
    {
        var repository = new EmailAccountRepository();
        var authorization = new StubMicrosoftEmailAuthorizationService(
            new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Pending,
                null,
                null,
                null,
                null));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            new StubEmailInboxReader(new EmailMailboxCheckpoint(42, 173, Now)),
            authorization,
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            CreateRequest() with
            {
                AuthenticationMethod = EmailAuthenticationMethod.Microsoft365OAuth,
                Password = null,
                MicrosoftAuthorizationId = "authorization"
            },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(repository.Account);
        Assert.Contains("not finished", Assert.Single(result.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingMicrosoft365AccountCanSaveMetadataWithoutSigningInAgain()
    {
        var checkpoint = new EmailMailboxCheckpoint(7, 99, Now.AddDays(-1));
        var existing = new EmailAccount
        {
            Id = "work",
            DisplayName = "Work",
            EmailAddress = "sam@work.example",
            Host = "outlook.office365.com",
            Port = 993,
            Security = EmailConnectionSecurity.SslOnConnect,
            AuthenticationMethod = EmailAuthenticationMethod.Microsoft365OAuth,
            Username = "sam@work.example",
            ProtectedOAuthTokenCache = "protected:serialized-cache",
            OAuthHomeAccountId = "home-account",
            InboxCheckpoint = checkpoint,
            SentCheckpoint = checkpoint
        };
        var repository = new EmailAccountRepository(existing);
        var reader = new StubEmailInboxReader(new EmailMailboxCheckpoint(42, 173, Now));
        var handler = new SaveEmailAccountUsecaseHandler(
            repository,
            new StubCredentialProtector(),
            reader,
            new StubMicrosoftEmailAuthorizationService(),
            new FixedTimeProvider(Now));

        var result = await handler.Handle(
            CreateRequest() with
            {
                EmailAccountId = existing.Id,
                DisplayName = "Work mailbox",
                EmailAddress = existing.EmailAddress,
                Host = existing.Host,
                AuthenticationMethod = EmailAuthenticationMethod.Microsoft365OAuth,
                Username = existing.Username,
                Password = null
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, reader.InboxCheckpointReadCount);
        Assert.Equal(0, reader.SentCheckpointReadCount);
        Assert.Same(checkpoint, repository.Account?.InboxCheckpoint);
        Assert.Equal("protected:serialized-cache", repository.Account?.ProtectedOAuthTokenCache);
    }

    private static SaveEmailAccountUsecase CreateRequest() => new(
        null,
        "Personal",
        "sam@example.com",
        "imap.example.com",
        993,
        EmailConnectionSecurity.SslOnConnect,
        EmailAuthenticationMethod.Password,
        "sam@example.com",
        "secret",
        null,
        "Personal mailbox guidance.",
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

    private sealed class StubEmailInboxReader(EmailMailboxCheckpoint checkpoint) : IEmailInboxReader
    {
        public int InboxCheckpointReadCount { get; private set; }
        public int SentCheckpointReadCount { get; private set; }

        public Task TestConnectionAsync(EmailAccount account, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<EmailMailboxCheckpoint> GetCurrentInboxCheckpointAsync(
            EmailAccount account,
            CancellationToken cancellationToken)
        {
            InboxCheckpointReadCount++;
            return Task.FromResult(checkpoint);
        }

        public Task<EmailMailboxCheckpoint?> GetCurrentSentCheckpointAsync(
            EmailAccount account,
            CancellationToken cancellationToken)
        {
            SentCheckpointReadCount++;
            return Task.FromResult<EmailMailboxCheckpoint?>(checkpoint);
        }

        public Task<NewEmailReadResult> ReadNewAsync(
            EmailAccount account,
            IReadOnlyCollection<string> ignoredSenderAddresses,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NewEmailReadResult> ReadNewSentAsync(
            EmailAccount account,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EmailMessageSnapshot>> ReadUnreadAsync(
            EmailAccount account,
            IReadOnlyCollection<string> ignoredSenderAddresses,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubMicrosoftEmailAuthorizationService(
        MicrosoftEmailAuthorizationCompletion? completion = null) : IMicrosoftEmailAuthorizationService
    {
        public void Finish(string authorizationId)
        {
        }

        public Task<MicrosoftEmailAuthorizationChallenge> StartAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MicrosoftEmailAuthorizationCompletion> CompleteAsync(
            string authorizationId,
            CancellationToken cancellationToken) => Task.FromResult(
                completion ?? throw new NotSupportedException());
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
