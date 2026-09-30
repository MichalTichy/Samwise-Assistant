using System.Text;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Teams.Configuration;

namespace Samwise.Inputs.Teams.InboundEvents.Transcripts;

public sealed class TeamsTranscriptInboundEvent : InboundEvent
{
    public string TranscriptId { get; set; } = string.Empty;
    public required TeamsTranscriptSource TranscriptSource { get; set; }
    public TeamsConnectedAccount? ConnectedAccount { get; set; }
    public string OrganizerId { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public bool IsOrganizedByConnectedAccount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public override string Source => "Teams transcript";
    public override string ExternalId =>
        $"{TranscriptSource.TranscriptKind}:{TranscriptSource.ContextId}:{TranscriptId}";

    public override string GetIntakeText()
    {
        var text = new StringBuilder()
            .AppendLine("Microsoft Teams call transcript")
            .Append("Connected account (you): ").AppendLine(FormatConnectedAccount())
            .Append("Organizer: ")
            .AppendLine(IsOrganizedByConnectedAccount ? $"You ({OrganizerName})" : OrganizerName)
            .Append("Created: ").AppendLine(CreatedAt.ToString("O"));
        if (EndedAt is not null)
        {
            text.Append("Ended: ").AppendLine(EndedAt.Value.ToString("O"));
        }

        text.Append("Transcript ID: ").AppendLine(TranscriptId)
            .Append("Transcript kind: ").AppendLine(TranscriptSource.TranscriptKind);
        TranscriptSource.AppendIntakeDetails(text);
        return text.AppendLine()
            .Append("The transcript content remains in Microsoft Teams. Use the Microsoft Teams transcript tools when its content is needed.")
            .ToString()
            .Trim();
    }

    private string FormatConnectedAccount() => ConnectedAccount is null
        ? "Unknown"
        : string.IsNullOrWhiteSpace(ConnectedAccount.Username)
            ? ConnectedAccount.DisplayName
            : $"{ConnectedAccount.DisplayName} <{ConnectedAccount.Username}>";
}
