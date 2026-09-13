namespace Samwise.Logic.Agents;

public sealed record ToolCatalogSnapshot(
    string CatalogVersion,
    string RawJson,
    IReadOnlyList<ToolServerCapability> Servers)
{
    public IReadOnlyList<ToolCapability> Tools { get; } = Servers
        .SelectMany(server => server.Tools)
        .ToList();
}
