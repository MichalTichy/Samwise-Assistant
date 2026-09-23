namespace Samwise.Inputs.Email;

public sealed class EmailCalendarInvitation
{
    public string Method { get; set; } = string.Empty;
    public string Uid { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string Organizer { get; set; } = string.Empty;
    public IReadOnlyList<string> Attendees { get; set; } = [];
    public string Status { get; set; } = string.Empty;
}
