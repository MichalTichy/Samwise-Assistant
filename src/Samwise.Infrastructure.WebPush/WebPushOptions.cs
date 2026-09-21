namespace Samwise.Infrastructure.WebPush;

public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>
    /// VAPID contact (<c>mailto:</c> or <c>https:</c>). Apple rejects pushes without a valid subject.
    /// Defaults to <c>mailto:</c> plus the configured owner e-mail.
    /// </summary>
    public string? Subject { get; set; }
}
