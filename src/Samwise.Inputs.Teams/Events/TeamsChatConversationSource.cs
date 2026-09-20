using System.Text;

namespace Samwise.Inputs.Teams.Events;

public sealed class TeamsChatConversationSource : TeamsConversationSource
{
    public string ChatId { get; set; } = string.Empty;
    public string ChatName { get; set; } = string.Empty;

    public override string ConversationKey => $"teams:chat:{ChatId}";
    public override string DisplayName => ChatName;
    public override string EventSource => "Teams chat conversation";
    public override string IntakeHeading => "Microsoft Teams chat conversation update";

    public override void AppendIntakeDetails(StringBuilder text) =>
        text.Append("Chat ID: ").AppendLine(ChatId);
}
