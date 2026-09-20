using System.Text;

namespace Samwise.Inputs.Teams.Events;

public sealed class TeamsAdhocCallTranscriptSource : TeamsTranscriptSource
{
    public string CallId { get; set; } = string.Empty;
    public override string TranscriptKind => "adhocCall";
    public override string ContextId => CallId;

    public override void AppendIntakeDetails(StringBuilder text) =>
        text.Append("Call ID: ").AppendLine(CallId);
}
