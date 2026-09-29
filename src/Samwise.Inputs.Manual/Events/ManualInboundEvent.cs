using Samwise.Domain.Inbox;

namespace Samwise.Inputs.Manual.Events;

public sealed class ManualInboundEvent : InboundEvent
{
    public string Input { get; set; } = string.Empty;
    public string? InputType { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Note { get; set; }
    public string ManualInputId { get; set; } = string.Empty;
    public override string Source => "Manual";
    public override string ExternalId => ManualInputId;

    public override string GetIntakeText()
    {
        if (string.IsNullOrWhiteSpace(InputType)
            && string.IsNullOrWhiteSpace(Title)
            && string.IsNullOrWhiteSpace(Url)
            && string.IsNullOrWhiteSpace(Note))
        {
            return Input.Trim();
        }

        var sections = new List<string>();
        AddField(sections, "Input type", InputType);
        AddField(sections, "Title", Title);
        AddField(sections, "URL", Url);
        sections.Add($"Text:{Environment.NewLine}{Input.Trim()}");

        if (!string.IsNullOrWhiteSpace(Note))
        {
            sections.Add($"Additional context:{Environment.NewLine}{Note.Trim()}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static void AddField(ICollection<string> sections, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sections.Add($"{label}: {value.Trim()}");
        }
    }
}
