namespace Samwise.Web.Pages.Tasks.Sources.Todoist;

public sealed record DismissedTodoistTask(string AgentTaskId, string TodoistTaskId, string Title, string Url,
    DateTimeOffset DismissedAt);
