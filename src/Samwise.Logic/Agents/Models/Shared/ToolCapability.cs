namespace Samwise.Logic.Agents;

public sealed record ToolCapability(
    string Id,
    string Name,
    string? Description,
    string InputSchemaJson,
    bool CanInvoke);
