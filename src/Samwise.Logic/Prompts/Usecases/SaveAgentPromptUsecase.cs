using MediatR;
using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record SaveAgentPromptUsecase(
    AgentStage Stage,
    string Prompt) : IRequest<AgentStageConfigurationSaveResult>;

internal sealed class SaveAgentPromptUsecaseHandler(
    IRepository<AgentConfiguration> repository,
    IPromptRenderer renderer,
    TimeProvider timeProvider)
    : IRequestHandler<SaveAgentPromptUsecase, AgentStageConfigurationSaveResult>
{
    public async Task<AgentStageConfigurationSaveResult> Handle(
        SaveAgentPromptUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = renderer.Validate(request.Stage, request.Prompt);
        if (errors.Count > 0)
        {
            return new AgentStageConfigurationSaveResult(false, errors);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            AgentConfiguration.DocumentId,
            cancellationToken);
        var current = configuration.GetStage(request.Stage);
        configuration.UpdateStage(
            request.Stage,
            request.Prompt.Trim(),
            current.Model,
            current.ReasoningEffort,
            timeProvider.GetUtcNow());
        await repository.UpdateAsync(configuration, cancellationToken);

        return AgentStageConfigurationSaveResult.Success;
    }
}
