namespace Samwise.Logic.Agents;

public sealed record ToolServerCapability(
    string Id,
    string Name,
    string Version,
    bool Required,
    string? Title,
    string? Description,
    string? WebsiteUrl,
    IReadOnlyList<ToolCapability> Tools);
