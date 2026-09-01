using MediatR;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record SaveAgentStageConfigurationUsecase(
    AgentStage Stage,
    string Prompt,
    string Model,
    string ReasoningEffort) : IRequest<AgentStageConfigurationSaveResult>;

internal sealed class SaveAgentStageConfigurationUsecaseHandler(
    IRepository<AgentConfiguration> repository,
    IAgentModelCatalog modelCatalog,
    IPromptRenderer renderer,
    TimeProvider timeProvider)
    : IRequestHandler<SaveAgentStageConfigurationUsecase, AgentStageConfigurationSaveResult>
{
    public async Task<AgentStageConfigurationSaveResult> Handle(
        SaveAgentStageConfigurationUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = renderer.Validate(request.Stage, request.Prompt).ToList();
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            errors.Add("The model cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.ReasoningEffort))
        {
            errors.Add("The reasoning effort cannot be empty.");
        }

        if (errors.Count > 0)
        {
            return new AgentStageConfigurationSaveResult(false, errors);
        }

        var modelId = request.Model.Trim();
        var reasoningEffort = request.ReasoningEffort.Trim();
        var models = await modelCatalog.GetModelsAsync(request.Stage, cancellationToken);
        var model = models.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, modelId, StringComparison.Ordinal));
        if (model is null)
        {
            return new AgentStageConfigurationSaveResult(
                false,
                [$"Model '{modelId}' is not available for the {request.Stage} stage."]);
        }

        if (!model.SupportedReasoningEfforts.Contains(reasoningEffort, StringComparer.Ordinal))
        {
            return new AgentStageConfigurationSaveResult(
                false,
                [$"Reasoning effort '{reasoningEffort}' is not supported by model '{modelId}'."]);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            AgentConfiguration.DocumentId,
            cancellationToken);
        configuration.UpdateStage(
            request.Stage,
            request.Prompt.Trim(),
            modelId,
            reasoningEffort,
            timeProvider.GetUtcNow());
        await repository.UpdateAsync(configuration, cancellationToken);

        return AgentStageConfigurationSaveResult.Success;
    }
}
