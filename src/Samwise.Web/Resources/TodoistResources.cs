using System.Globalization;
using System.Resources;

namespace Samwise.Web.Resources;

internal static class TodoistResources
{
    private static readonly ResourceManager Manager = new(typeof(TodoistResources));
    public static string Intro => Manager.GetString(nameof(Intro), CultureInfo.CurrentUICulture)!;
    public static string ScopeTitle => Manager.GetString(nameof(ScopeTitle), CultureInfo.CurrentUICulture)!;
    public static string ScopeDescription => Manager.GetString(nameof(ScopeDescription), CultureInfo.CurrentUICulture)!;
    public static string ProjectExclusions => Manager.GetString(nameof(ProjectExclusions), CultureInfo.CurrentUICulture)!;
    public static string LabelExclusions => Manager.GetString(nameof(LabelExclusions), CultureInfo.CurrentUICulture)!;
    public static string DismissedTitle => Manager.GetString(nameof(DismissedTitle), CultureInfo.CurrentUICulture)!;
    public static string DismissedDescription => Manager.GetString(nameof(DismissedDescription), CultureInfo.CurrentUICulture)!;
    public static string NoDismissedTasks => Manager.GetString(nameof(NoDismissedTasks), CultureInfo.CurrentUICulture)!;
    public static string DismissedAt => Manager.GetString(nameof(DismissedAt), CultureInfo.CurrentUICulture)!;
    public static string DismissedHistory => Manager.GetString(nameof(DismissedHistory), CultureInfo.CurrentUICulture)!;
    public static string Restore => Manager.GetString(nameof(Restore), CultureInfo.CurrentUICulture)!;
    public static string Restored => Manager.GetString(nameof(Restored), CultureInfo.CurrentUICulture)!;
}
