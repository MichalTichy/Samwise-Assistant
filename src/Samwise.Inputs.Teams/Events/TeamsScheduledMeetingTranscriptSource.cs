using System.Text;

namespace Samwise.Inputs.Teams.Events;

public sealed class TeamsScheduledMeetingTranscriptSource : TeamsTranscriptSource
{
    public string MeetingId { get; set; } = string.Empty;
    public override string TranscriptKind => "scheduledMeeting";
    public override string ContextId => MeetingId;

    public override void AppendIntakeDetails(StringBuilder text) =>
        text.Append("Meeting ID: ").AppendLine(MeetingId);
}
