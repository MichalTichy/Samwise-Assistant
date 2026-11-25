using System.Globalization;
using System.Text.Json;
using Samwise.Infrastructure.Todoist.Api;
using Samwise.Inputs.Todoist.Authentication;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Discovery;
using Samwise.Inputs.Todoist.InboundEvents;
using Samwise.Inputs.Todoist.Ingestion;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Todoist.Readers;

internal sealed class TodoistRestTaskReader(
    TodoistApiClient client,
    IReadOnlyRepository<TodoistInputConfiguration> configurationRepository,
    ITodoistCredentialProtector credentialProtector) : ITodoistTaskReader
{
    public async Task<TodoistTaskReadResult> ReadDueTasksAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        var user = await client.GetAsync("user", token, cancellationToken);
        var projects = await client.GetCollectionAsync("projects", token, cancellationToken);
        var projectNames = projects
            .Select(project => (Id: GetString(project, "id"), Name: GetString(project, "name")))
            .Where(project => project.Id is not null)
            .GroupBy(project => project.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name ?? group.Key, StringComparer.Ordinal);
        var tasks = await client.GetCollectionAsync("tasks", token, cancellationToken);
        return new TodoistTaskReadResult(
            tasks.Select(task => MapTask(task, projectNames)).OfType<TodoistTaskSnapshot>().ToList(),
            ReadTimeZone(user), GetString(user, "id"));
    }

    public async Task<TodoistTaskState> ReadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        JsonElement data;
        try { data = await client.GetAsync($"tasks/{Uri.EscapeDataString(taskId)}", token, cancellationToken); }
        catch (TodoistApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        { return new TodoistTaskState("unavailable"); }
        if (GetBoolean(data, "is_deleted")) return new TodoistTaskState("deleted");
        if (GetBoolean(data, "checked") || GetString(data, "completed_at") is not null)
            return new TodoistTaskState("completed");
        var task = MapTask(data, new Dictionary<string, string>());
        if (task is null) return new TodoistTaskState("undated");
        var user = await client.GetAsync("user", token, cancellationToken);
        return new TodoistTaskState("active", task, ReadTimeZone(user), GetString(user, "id"));
    }

    public async Task<IReadOnlyList<TodoistCommentSnapshot>> ReadCommentsAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var token = await GetTokenAsync(cancellationToken);
        var comments = await client.GetCollectionAsync(
            $"comments?task_id={Uri.EscapeDataString(taskId.Trim())}",
            token,
            cancellationToken);
        return comments
            .Where(comment => !GetBoolean(comment, "is_deleted"))
            .Select(MapComment)
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Content))
            .ToList();
    }

    public async Task<TodoistInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        var user = await client.GetAsync("user", token, cancellationToken);
        var projects = await client.GetCollectionAsync("projects", token, cancellationToken);
        var labels = await client.GetCollectionAsync("labels", token, cancellationToken);
        return new TodoistInputCatalog(
            GetString(user, "full_name") ?? GetString(user, "email"),
            projects
                .Where(project => !GetBoolean(project, "is_archived") && !GetBoolean(project, "is_deleted"))
                .Select(project => (Id: GetString(project, "id"), Name: GetString(project, "name")))
                .Where(project => project.Id is not null)
                .Select(project => new TodoistInputSourceOption(project.Id!, project.Name ?? project.Id!))
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            labels
                .Select(label => GetString(label, "name"))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(name => new TodoistInputSourceOption(name, name))
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    internal static TodoistTaskSnapshot? MapTask(
        JsonElement task,
        IReadOnlyDictionary<string, string> projectNames)
    {
        var id = GetString(task, "id");
        if (id is null
            || GetBoolean(task, "checked")
            || GetBoolean(task, "is_deleted")
            || !task.TryGetProperty("due", out var due)
            || due.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dueDate = GetString(due, "datetime") ?? GetString(due, "date");
        if (string.IsNullOrWhiteSpace(dueDate))
        {
            return null;
        }

        var projectId = GetString(task, "project_id") ?? string.Empty;
        var labels = task.TryGetProperty("labels", out var labelValues) && labelValues.ValueKind == JsonValueKind.Array
            ? labelValues.EnumerateArray()
                .Where(label => label.ValueKind == JsonValueKind.String)
                .Select(label => label.GetString()!)
                .ToList()
            : [];
        // A deadline is a calendar date without time or timezone; an unreadable value is dropped, not fatal.
        var deadline = task.TryGetProperty("deadline", out var deadlineValue)
            && deadlineValue.ValueKind == JsonValueKind.Object
            && DateOnly.TryParseExact(
                GetString(deadlineValue, "date"),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var deadlineDate)
                ? deadlineDate
                : (DateOnly?)null;
        return new TodoistTaskSnapshot(
            id,
            GetString(task, "content") ?? string.Empty,
            GetString(task, "description") ?? string.Empty,
            projectId,
            projectNames.GetValueOrDefault(projectId),
            labels,
            task.TryGetProperty("priority", out var priority) && priority.TryGetInt32(out var value) ? value : 1,
            new TodoistDue
            {
                Date = dueDate.Trim(),
                Timezone = GetString(due, "timezone"),
                Text = GetString(due, "string"),
                IsRecurring = GetBoolean(due, "is_recurring")
            },
            deadline,
            $"https://app.todoist.com/app/task/{Uri.EscapeDataString(id)}",
            task.TryGetProperty("completed_count", out var count) && count.TryGetInt32(out var completedCount) ? completedCount : null,
            GetString(task, "responsible_uid"));
    }

    internal static TodoistCommentSnapshot MapComment(JsonElement comment) => new(
        GetString(comment, "id") ?? string.Empty,
        DateTimeOffset.TryParse(
            GetString(comment, "posted_at"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var postedAt)
            ? postedAt
            : null,
        GetString(comment, "content") ?? string.Empty);

    internal static string? ReadTimeZone(JsonElement user) =>
        user.TryGetProperty("tz_info", out var tzInfo) && tzInfo.ValueKind == JsonValueKind.Object
            ? GetString(tzInfo, "timezone")
            : null;

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurationRepository.GetRequiredByIdAsync(
            TodoistInputConfiguration.DocumentId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(configuration.ProtectedApiToken))
        {
            throw new InvalidOperationException("No Todoist API token is configured.");
        }

        return credentialProtector.Unprotect(configuration.ProtectedApiToken);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        } : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
