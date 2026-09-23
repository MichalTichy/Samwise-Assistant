using System.Text;

namespace Samwise.Inputs.Email.Events;

internal static class EmailCalendarInvitationText
{
    public static void Append(StringBuilder text, EmailCalendarInvitation? invitation)
    {
        if (invitation is null)
        {
            return;
        }

        text.AppendLine().AppendLine().AppendLine("Calendar invitation metadata:")
            .Append("Method: ").AppendLine(invitation.Method)
            .Append("UID: ").AppendLine(invitation.Uid)
            .Append("Summary: ").AppendLine(invitation.Summary)
            .Append("Start: ").AppendLine(invitation.Start)
            .Append("End: ").AppendLine(invitation.End)
            .Append("Time zone: ").AppendLine(invitation.TimeZoneId)
            .Append("Location: ").AppendLine(invitation.Location)
            .Append("Organizer: ").AppendLine(invitation.Organizer)
            .Append("Attendees: ").AppendLine(string.Join(", ", invitation.Attendees))
            .Append("Status: ").AppendLine(invitation.Status);
    }
}
