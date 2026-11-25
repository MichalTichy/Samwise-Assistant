namespace Samwise.Logic.WorkItems;

public static class WorkItemRoutes
{
    private const string Prefix = "/work-items/";
    public const string DetailTemplate = Prefix + "{WorkItemId}";

    public static string Detail(string workItemId) => Prefix + Uri.EscapeDataString(workItemId);
}
