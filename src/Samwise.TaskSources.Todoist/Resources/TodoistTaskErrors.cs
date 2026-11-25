using System.Globalization;
using System.Resources;

namespace Samwise.TaskSources.Todoist.Resources;

public static class TodoistTaskErrors
{
    private static readonly ResourceManager Manager = new(typeof(TodoistTaskErrors));
    public static string TaskNotFound => Manager.GetString(nameof(TaskNotFound), CultureInfo.CurrentUICulture)!;
    public static string NotTodoistTask => Manager.GetString(nameof(NotTodoistTask), CultureInfo.CurrentUICulture)!;
    public static string NotDismissed => Manager.GetString(nameof(NotDismissed), CultureInfo.CurrentUICulture)!;
}
