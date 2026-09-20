using System.Text;
using System.Text.Json.Serialization;

namespace Samwise.Inputs.Teams.Events;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TeamsChatConversationSource), "chat")]
[JsonDerivedType(typeof(TeamsChannelThreadConversationSource), "channelThread")]
public abstract class TeamsConversationSource
{
    public abstract string ConversationKey { get; }
    public abstract string DisplayName { get; }
    public abstract string EventSource { get; }
    public abstract string IntakeHeading { get; }

    public abstract void AppendIntakeDetails(StringBuilder text);
}
