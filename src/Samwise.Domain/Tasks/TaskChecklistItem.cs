namespace Samwise.Domain.Tasks;

public sealed class TaskChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string? Evidence { get; set; }

    public TaskChecklistItem Copy(bool reset = false) => new()
    {
        Id = Id, Text = Text,
        IsCompleted = !reset && IsCompleted,
        Evidence = reset ? null : Evidence
    };
}
