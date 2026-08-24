using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Agents;

internal sealed class AgentInvocationRecorder(
    IRepository<AgentInvocation> repository,
    TimeProvider timeProvider)
{
    public async Task<AgentInvocation> StartAsync(
        string profile,
        string renderedPrompt,
        string? inboundEventId,
        string? taskId,
        int taskRevision,
        CancellationToken cancellationToken)
    {
        var invocation = new AgentInvocation
        {
            Id = Guid.NewGuid().ToString(),
            InboundEventId = inboundEventId,
            TaskId = taskId,
            TaskRevision = taskRevision,
            Profile = profile,
            Provider = "Pending",
            RenderedPrompt = renderedPrompt,
            Status = AgentInvocationStatus.Running,
            StartedAt = timeProvider.GetUtcNow()
        };

        return await repository.AddAsync(invocation, cancellationToken);
    }

    public Task SucceedAsync(
        AgentInvocation invocation,
        AgentReceipt receipt,
        CancellationToken cancellationToken)
    {
        invocation.Provider = receipt.Provider;
        invocation.Model = receipt.Model;
        invocation.CompletionId = receipt.CompletionId;
        invocation.RawResponse = receipt.RawResponse;
        invocation.Status = AgentInvocationStatus.Succeeded;
        invocation.FinishedAt = timeProvider.GetUtcNow();
        return repository.UpdateAsync(invocation, cancellationToken);
    }

    public Task FailAsync(
        AgentInvocation invocation,
        AgentInvocationStatus status,
        Exception exception,
        CancellationToken cancellationToken)
    {
        invocation.Status = status;
        invocation.Error = exception.Message;
        invocation.FinishedAt = timeProvider.GetUtcNow();
        return repository.UpdateAsync(invocation, cancellationToken);
    }
}
