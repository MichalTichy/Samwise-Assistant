namespace Samwise.Logic.Agents;

public interface IAgentRuntime
{
    Task<IntakeResult> ClassifyAsync(
        IntakeRequest request,
        CancellationToken cancellationToken = default);

    Task<PlannerResult> PlanAsync(PlannerRequest request, CancellationToken cancellationToken = default);

    Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default);
}
