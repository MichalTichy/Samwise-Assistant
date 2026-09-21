using MediatR;
using Samwise.Inputs.Todoist.Authentication;
using Samwise.Inputs.Todoist.Configuration;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Todoist.Usecases.Configuration;

public sealed record SaveTodoistInputConfigurationUsecase(
    string? ApiToken,
    bool RemoveApiToken,
    int PollingIntervalMinutes,
    int LeadHours,
    int OverdueDays,
    string? IntakeGuidance,
    IReadOnlyList<TodoistInputSource> IgnoredProjects,
    IReadOnlyList<TodoistInputSource> IgnoredLabels)
    : IRequest<TodoistInputConfigurationSaveResult>;

internal sealed class SaveTodoistInputConfigurationUsecaseHandler(
    IRepository<TodoistInputConfiguration> repository,
    ITodoistCredentialProtector credentialProtector)
    : IRequestHandler<SaveTodoistInputConfigurationUsecase, TodoistInputConfigurationSaveResult>
{
    public async Task<TodoistInputConfigurationSaveResult> Handle(
        SaveTodoistInputConfigurationUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new TodoistInputConfigurationSaveResult(false, errors);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            TodoistInputConfiguration.DocumentId,
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
            request.IgnoredLabels);
        await repository.UpdateAsync(configuration, cancellationToken);
        return new TodoistInputConfigurationSaveResult(true, []);
    }

    private static List<string> Validate(SaveTodoistInputConfigurationUsecase request)
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

        if (request.IgnoredProjects.Concat(request.IgnoredLabels).Any(source => string.IsNullOrWhiteSpace(source.Id)))
        {
            errors.Add("Every ignored Todoist project or label must have an ID.");
        }

        return errors;
    }
}
