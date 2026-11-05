using System.Globalization;
using System.Resources;

namespace Samwise.Web.Resources;

internal static class WorkItemResources
{
    private static readonly ResourceManager Manager = new(typeof(WorkItemResources));

    public static string AwaitingExternalEvent =>
        Manager.GetString(nameof(AwaitingExternalEvent), CultureInfo.CurrentUICulture)!;

    public static string Discard => Manager.GetString(nameof(Discard), CultureInfo.CurrentUICulture)!;

    public static string DiscardWorkItem => Manager.GetString(nameof(DiscardWorkItem), CultureInfo.CurrentUICulture)!;

    public static string DiscardConfirmation => Manager.GetString(nameof(DiscardConfirmation), CultureInfo.CurrentUICulture)!;

    public static string DiscardReasonHelp => Manager.GetString(nameof(DiscardReasonHelp), CultureInfo.CurrentUICulture)!;

    public static string DiscardReasonPlaceholder => Manager.GetString(nameof(DiscardReasonPlaceholder), CultureInfo.CurrentUICulture)!;

    public static string Discarding => Manager.GetString(nameof(Discarding), CultureInfo.CurrentUICulture)!;

    public static string Cancel => Manager.GetString(nameof(Cancel), CultureInfo.CurrentUICulture)!;
}
