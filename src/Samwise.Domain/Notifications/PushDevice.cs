using System.Security.Cryptography;
using System.Text;
using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Notifications;

/// <summary>One browser Web Push subscription, such as the Samwise Home Screen app on an iPhone.</summary>
public sealed class PushDevice : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>The endpoint identifies a subscription, so re-subscribing the same device updates it.</summary>
    public static string CreateId(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.Trim())));
    }

    public void RecordDelivered(DateTimeOffset now)
    {
        LastSucceededAt = now;
        LastError = null;
    }

    public void RecordFailed(string error, DateTimeOffset now)
    {
        LastFailedAt = now;
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown error." : error.Trim();
    }
}
