using MediatR;
using Samwise.TaskSources.Todoist.Authentication;
using Samwise.TaskSources.Todoist.Configuration;
using Samwise.TaskSources.Todoist.Synchronization;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.TaskSources.Todoist.Usecases.Configuration;

public sealed record SaveTodoistTaskSourceConfigurationUsecase(
    string? ApiToken,
    bool RemoveApiToken,
    int PollingIntervalMinutes,
    int LeadHours,
    int OverdueDays,
    string? IntakeGuidance,
    IReadOnlyList<TodoistSourceSelection> IgnoredProjects,
    IReadOnlyList<TodoistSourceSelection> IgnoredLabels,
    IReadOnlyList<TodoistSourceSelection>? IncludedProjects = null,
    IReadOnlyList<TodoistSourceSelection>? IncludedLabels = null)
    : IRequest<TodoistTaskSourceConfigurationSaveResult>;

internal sealed class SaveTodoistTaskSourceConfigurationUsecaseHandler(
    IRepository<TodoistTaskSourceConfiguration> repository,
    ITodoistCredentialProtector credentialProtector,
    ITodoistSynchronizationLock processingLock)
    : IRequestHandler<SaveTodoistTaskSourceConfigurationUsecase, TodoistTaskSourceConfigurationSaveResult>
{
    public async Task<TodoistTaskSourceConfigurationSaveResult> Handle(
        SaveTodoistTaskSourceConfigurationUsecase request,
        CancellationToken cancellationToken)
    {
        using var lease = await processingLock.EnterAsync(cancellationToken);
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new TodoistTaskSourceConfigurationSaveResult(false, errors);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            TodoistTaskSourceConfiguration.DocumentId,
            cancellationToken);
        var protectedApiToken = request.RemoveApiToken
            ? null
            : string.IsNullOrWhiteSpace(request.ApiToken)
                ? configuration.ProtectedApiToken
                : credentialProtector.Protect(request.ApiToken.Trim());
        configuration.Update(
            protectedApiToken,
            request.PollingIntervalMinutes,
            request.LeadHours,
            request.OverdueDays,
            request.IntakeGuidance,
            request.IgnoredProjects,
            request.IgnoredLabels,
            request.IncludedProjects,
            request.IncludedLabels);
        await repository.UpdateAsync(configuration, cancellationToken);
        return new TodoistTaskSourceConfigurationSaveResult(true, []);
    }

    private static List<string> Validate(SaveTodoistTaskSourceConfigurationUsecase request)
    {
        var errors = new List<string>();
        if (request.PollingIntervalMinutes is < 1 or > 1_440)
        {
            errors.Add("The polling interval must be between 1 and 1,440 minutes.");
        }

        if (request.LeadHours is < 0 or > 720)
        {
            errors.Add("The lead time must be between 0 and 720 hours.");
        }

        if (request.OverdueDays is < 0 or > 365)
        {
            errors.Add("The overdue range must be between 0 and 365 days.");
        }

        if (request.IgnoredProjects.Concat(request.IgnoredLabels).Concat(request.IncludedProjects ?? [])
            .Concat(request.IncludedLabels ?? []).Any(source => string.IsNullOrWhiteSpace(source.Id)))
        {
            errors.Add("Every Todoist project or label selection must have an ID.");
        }

        return errors;
    }
}
