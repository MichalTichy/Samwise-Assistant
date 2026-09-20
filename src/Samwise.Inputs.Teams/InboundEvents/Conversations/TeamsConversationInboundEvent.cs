using System.Text;
using Samwise.Domain.Inbox;

namespace Samwise.Inputs.Teams.InboundEvents.Conversations;

public sealed class TeamsConversationInboundEvent : InboundEvent
{
    public string ConversationVersion { get; set; } = string.Empty;
    public required TeamsConversationSource Conversation { get; set; }
    public List<TeamsConversationMessage> Messages { get; set; } = [];

    public override string Source => Conversation.EventSource;
    public override string ExternalId => $"{Conversation.ConversationKey}:{ConversationVersion}";

    public override string GetIntakeText()
    {
        var text = new StringBuilder()
            .AppendLine(Conversation.IntakeHeading)
            .Append("Conversation: ").AppendLine(Conversation.DisplayName)
            .Append("Conversation key: ").AppendLine(Conversation.ConversationKey);
        Conversation.AppendIntakeDetails(text);
        text.AppendLine().AppendLine("New messages triggering this intake:");
        foreach (var message in Messages.OrderBy(message => message.SentAt))
        {
            text.AppendLine()
                .Append('[').Append(message.SentAt.ToString("O")).Append("] ")
                .Append(message.SenderName)
                .Append(" (message ").Append(message.MessageId).AppendLine(")")
                .AppendLine(message.BodyText.Trim());
        }

        return text.ToString().Trim();
    }
}
