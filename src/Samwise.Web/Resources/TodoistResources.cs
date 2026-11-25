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
}
