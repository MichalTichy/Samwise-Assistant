# Samwise.Infrastructure.Todoist

Direct [Todoist API v1](https://developer.todoist.com/api/v1/) adapter and polling host for `Samwise.TaskSources.Todoist`.

- `Api/TodoistApiClient` is a typed `HttpClient` for `https://api.todoist.com/api/v1/`. It follows `next_cursor` pagination and retries `429` responses after `Retry-After`.
- `Readers/TodoistRestTaskReader` implements `ITodoistTaskReader`. It lists all active tasks with `GET tasks` and leaves the source exclusions and responsibility decisions to the module, so it does not depend on Todoist filter-query syntax. It also reads comments for tasks being synchronized, and the project/label catalogue and account timezone from `GET projects`, `GET labels`, and `GET user`.
- `Authentication/DataProtectionTodoistCredentialProtector` protects the UI-entered API token with ASP.NET Data Protection.
- `Polling/TodoistPollingBackgroundService` sends `SyncTodoistTasksUsecase` at the configured interval.

The adapter only reads. Writes to Todoist belong to the execution agent through the Gateway Todoist MCP server.
