using System.Text;

namespace Samwise.Inputs.Teams.Events;

public sealed class TeamsChannelThreadConversationSource : TeamsConversationSource
{
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ThreadRootMessageId { get; set; } = string.Empty;

    public override string ConversationKey =>
        $"teams:channel:{TeamId}:{ChannelId}:thread:{ThreadRootMessageId}";

    public override string DisplayName => $"{TeamName} / {ChannelName}";
    public override string EventSource => "Teams channel conversation";
    public override string IntakeHeading => "Microsoft Teams channel conversation update";

    public override void AppendIntakeDetails(StringBuilder text) =>
        text.Append("Team ID: ").AppendLine(TeamId)
            .Append("Channel ID: ").AppendLine(ChannelId)
            .Append("Thread root message ID: ").AppendLine(ThreadRootMessageId);
}
