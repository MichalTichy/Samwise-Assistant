using System.Text.Json;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.Agents;
using Samwise.Logic.Playbooks.Specifications;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Logic.Prompts.Services;

internal sealed class AgentPromptBuilder(
    IReadOnlyRepository<InboundEvent> inboundEventRepository,
    IReadOnlyRepository<DomainWorkItem> workItemRepository,
    IReadOnlyRepository<AgentConfiguration> agentConfigurationRepository,
    IReadOnlyRepository<Playbook> playbookRepository,
    IAgentToolCatalog toolCatalog,
    IAgentPromptPlaceholderCatalog placeholderCatalog,
    IPromptRenderer renderer,
    TimeProvider timeProvider) : IAgentPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IntakeAgentPrompt> BuildIntakeAsync(
        string inboundEventId,
        CancellationToken cancellationToken)
    {
        var inboundEvent = await inboundEventRepository.GetRequiredByIdAsync(inboundEventId, cancellationToken);
        var configuration = await GetConfigurationAsync(cancellationToken);
        var activeWorkItems = await workItemRepository.ListAsync(
            new ActiveWorkItemCandidatesSpecification(), cancellationToken);
        var intakeToolsTask = toolCatalog.GetToolsAsync(AgentStage.Intake, cancellationToken);
        var planningToolsTask = toolCatalog.GetToolsAsync(AgentStage.Planning, cancellationToken);
        var executionToolsTask = toolCatalog.GetToolsAsync(AgentStage.Execution, cancellationToken);
        await Task.WhenAll(intakeToolsTask, planningToolsTask, executionToolsTask);
        var intakeTools = await intakeToolsTask;
        var planningTools = await planningToolsTask;
        var executionTools = await executionToolsTask;
        var stageConfiguration = configuration.GetStage(AgentStage.Intake);
        var text = Render(
            AgentStage.Intake,
            stageConfiguration.Prompt,
            CreateValues(
                AgentStage.Intake,
                (AgentPromptPlaceholders.InputText, inboundEvent.GetIntakeText()),
                (AgentPromptPlaceholders.ActiveWorkItems, JsonSerializer.Serialize(
                    activeWorkItems.Select(workItem => new
                    {
                        id = workItem.Id,
                        title = workItem.Title,
                        description = Truncate(workItem.Description, 1_000),
                        status = workItem.Status.ToString(),
                        revision = workItem.Revision,
                        updated_at = workItem.UpdatedAt
                    }),
                    JsonOptions)),
                (AgentPromptPlaceholders.PlanningTools, planningTools.RawJson),
                (AgentPromptPlaceholders.ExecutionTools, executionTools.RawJson),
                (AgentPromptPlaceholders.CurrentTime, timeProvider.GetUtcNow().ToString("O"))));

        return new IntakeAgentPrompt(
            text,
            CreateRunConfiguration(stageConfiguration),
            intakeTools,
            activeWorkItems);
    }

    public async Task<PlanningAgentPrompt> BuildPlanningAsync(
        string workItemId,
        CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(workItemId, cancellationToken);
        var configuration = await GetConfigurationAsync(cancellationToken);
        var playbooks = await playbookRepository.ListAsync(
            new PlaybooksSpecification(enabledOnly: true), cancellationToken);
        var planningToolsTask = toolCatalog.GetToolsAsync(AgentStage.Planning, cancellationToken);
        var executionToolsTask = toolCatalog.GetToolsAsync(AgentStage.Execution, cancellationToken);
        await Task.WhenAll(planningToolsTask, executionToolsTask);
        var planningTools = await planningToolsTask;
        var executionTools = await executionToolsTask;
        var stageConfiguration = configuration.GetStage(AgentStage.Planning);
        var text = Render(
            AgentStage.Planning,
            stageConfiguration.Prompt,
            CreateValues(
                AgentStage.Planning,
                (AgentPromptPlaceholders.WorkItemId, workItem.Id),
                (AgentPromptPlaceholders.WorkItemTitle, workItem.Title),
                (AgentPromptPlaceholders.WorkItemDescription, workItem.Description),
                (AgentPromptPlaceholders.WorkItemRevision, workItem.Revision.ToString()),
                (AgentPromptPlaceholders.WorkItemComments, FormatComments(workItem.Comments)),
                (AgentPromptPlaceholders.PreviousPlan, workItem.FindLatestPlan() is { } previousPlan
                    ? JsonSerializer.Serialize(previousPlan, JsonOptions)
                    : string.Empty),
                (AgentPromptPlaceholders.RejectionFeedback, workItem.LatestPlanningFeedback),
                (AgentPromptPlaceholders.Playbooks, JsonSerializer.Serialize(
                    playbooks.Select(playbook => new
                    {
                        id = playbook.Id,
                        name = playbook.Name,
                        description = playbook.Description
                    }),
                    JsonOptions)),
                (AgentPromptPlaceholders.ExecutionTools, executionTools.RawJson),
                (AgentPromptPlaceholders.CurrentTime, timeProvider.GetUtcNow().ToString("O"))));

        return new PlanningAgentPrompt(
            text,
            CreateRunConfiguration(stageConfiguration),
            planningTools,
            executionTools,
            playbooks,
            configuration.Id,
            stageConfiguration.UpdatedAt);
    }

    public async Task<ExecutionAgentPrompt> BuildExecutionAsync(
        string workItemId,
        int workItemRevision,
        string planId,
        CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(workItemId, cancellationToken);
        if (workItem.Revision != workItemRevision ||
            !string.Equals(workItem.CurrentPlanId, planId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The work item changed before its execution prompt was built.");
        }

        var plan = workItem.FindCurrentPlan()
            ?? throw new InvalidOperationException("The approved plan is missing from the work item document.");
        var configuration = await GetConfigurationAsync(cancellationToken);
        var executionTools = await toolCatalog.GetToolsAsync(AgentStage.Execution, cancellationToken);
        var stageConfiguration = configuration.GetStage(AgentStage.Execution);
        var text = Render(
            AgentStage.Execution,
            stageConfiguration.Prompt,
            CreateValues(
                AgentStage.Execution,
                (AgentPromptPlaceholders.WorkItemId, workItem.Id),
                (AgentPromptPlaceholders.WorkItemTitle, workItem.Title),
                (AgentPromptPlaceholders.WorkItemDescription, workItem.Description),
                (AgentPromptPlaceholders.WorkItemRevision, workItem.Revision.ToString()),
                (AgentPromptPlaceholders.WorkItemComments, FormatComments(workItem.Comments)),
                (AgentPromptPlaceholders.ApprovedPlan, JsonSerializer.Serialize(plan, JsonOptions)),
                (AgentPromptPlaceholders.ExecutionTools, executionTools.RawJson),
                (AgentPromptPlaceholders.CurrentTime, timeProvider.GetUtcNow().ToString("O"))));

        return new ExecutionAgentPrompt(
            text,
            CreateRunConfiguration(stageConfiguration),
            executionTools);
    }

    private Task<AgentConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        agentConfigurationRepository.GetRequiredByIdAsync(
            AgentConfiguration.DocumentId,
            cancellationToken);

    private string Render(
        AgentStage stage,
        string template,
        IReadOnlyDictionary<string, string?> values)
    {
        var result = renderer.Render(stage, template, values);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors));
        }

        return result.Text;
    }

    private IReadOnlyDictionary<string, string?> CreateValues(
        AgentStage stage,
        params (string Placeholder, string? Value)[] values)
    {
        var allowed = placeholderCatalog.GetForStage(stage)
            .Select(placeholder => placeholder.Key)
            .ToHashSet(StringComparer.Ordinal);
        var unavailable = values.FirstOrDefault(value => !allowed.Contains(value.Placeholder));
        if (unavailable.Placeholder is not null)
        {
            throw new InvalidOperationException(
                $"Prompt placeholder '{unavailable.Placeholder}' is not available for the {stage} stage.");
        }

        return values.ToDictionary(
            value => value.Placeholder,
            value => value.Value,
            StringComparer.Ordinal);
    }

    private static AgentRunConfiguration CreateRunConfiguration(AgentStageConfiguration configuration) =>
        new(configuration.Model, configuration.ReasoningEffort);

    private static string FormatComments(IReadOnlyList<WorkItemComment> comments) => comments.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, comments.Select(comment => $"- [{comment.AuthorKind}] {comment.Body}"));

    private static string Truncate(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : value[..maximumLength] + "…";
}
