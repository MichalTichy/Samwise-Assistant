using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Inputs.Email;

public enum EmailConnectionSecurity
{
    SslOnConnect,
    StartTls
}

public sealed class EmailAccount : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public EmailConnectionSecurity Security { get; set; }
    public EmailAuthenticationMethod AuthenticationMethod { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    public string ProtectedOAuthTokenCache { get; set; } = string.Empty;
    public string? OAuthHomeAccountId { get; set; }
    public bool IsEnabled { get; set; }
    public EmailInboxCheckpoint? InboxCheckpoint { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastCheckStartedAt { get; set; }
    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
    public string? LastError { get; set; }

    public void RecordCheckStarted(DateTimeOffset now)
    {
        LastCheckStartedAt = now;
        LastError = null;
    }

    public void RecordCheckSucceeded(DateTimeOffset now)
    {
        LastSuccessfulCheckAt = now;
        LastError = null;
    }

    public void RecordCheckFailed(string error)
    {
        LastError = string.IsNullOrWhiteSpace(error)
            ? "The mailbox check failed."
            : error.Trim();
    }
}

public sealed record EmailInboxCheckpoint(
    uint UidValidity,
    uint NextUid,
    DateTimeOffset UpdatedAt);
