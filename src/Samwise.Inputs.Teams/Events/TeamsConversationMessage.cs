namespace Samwise.Inputs.Teams.Events;

public sealed class TeamsConversationMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? ReplyToMessageId { get; set; }
    public string? SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public string? WebUrl { get; set; }
}
