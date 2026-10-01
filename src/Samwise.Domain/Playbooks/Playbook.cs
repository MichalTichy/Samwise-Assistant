using Shared.Infrastructure.Persistence;

namespace Samwise.Domain.Playbooks;

public sealed class Playbook : IItemWithId
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Applicability { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool SuitableForAutomaticApproval { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
