using System.Text;
using System.Text.Json.Serialization;

namespace Samwise.Inputs.Teams.InboundEvents.Transcripts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TeamsScheduledMeetingTranscriptSource), "scheduledMeeting")]
[JsonDerivedType(typeof(TeamsAdhocCallTranscriptSource), "adhocCall")]
public abstract class TeamsTranscriptSource
{
    public abstract string TranscriptKind { get; }
    public abstract string ContextId { get; }

    public abstract void AppendIntakeDetails(StringBuilder text);
}
