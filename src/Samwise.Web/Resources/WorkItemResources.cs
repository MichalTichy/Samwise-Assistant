using System.Globalization;
using System.Resources;

namespace Samwise.Web.Resources;

internal static class WorkItemResources
{
    private static readonly ResourceManager Manager = new(typeof(WorkItemResources));

    public static string AwaitingExternalEvent =>
        Manager.GetString(nameof(AwaitingExternalEvent), CultureInfo.CurrentUICulture)!;
}
