using Shared.Infrastructure.Persistence;

namespace Samwise.Logic.Notifications;

/// <summary>The VAPID key pair identifying this Samwise instance to push services.</summary>
public sealed class PushApplicationKeys : IItemWithId
{
    public const string DocumentId = "push-application-keys";

    public string Id { get; set; } = DocumentId;

    /// <summary>Uncompressed P-256 public key, base64url.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>P-256 private key, protected with ASP.NET Data Protection.</summary>
    public string ProtectedPrivateKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
