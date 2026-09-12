using Samwise.Domain.Agents;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Agents.Services;

internal sealed record AgentInvocationStart(
    string Profile,
    string RenderedPrompt,
    string? InboundEventId,
    string? WorkItemId,
    int WorkItemRevision,
    AgentRunConfiguration Configuration,
    ToolCatalogSnapshot? AvailableTools = null);

internal interface IAgentInvocationService
{
    Task<AgentInvocation> StartAsync(AgentInvocationStart request, CancellationToken cancellationToken);

    Task CompleteAsync(string agentInvocationId, AgentReceipt receipt, CancellationToken cancellationToken);

    Task FailAsync(
        string agentInvocationId,
        AgentInvocationStatus status,
        string error,
        CancellationToken cancellationToken);
}

internal sealed class AgentInvocationService(
    IRepository<AgentInvocation> repository,
    TimeProvider timeProvider) : IAgentInvocationService
{
    public Task<AgentInvocation> StartAsync(
        AgentInvocationStart request,
        CancellationToken cancellationToken)
    {
        var invocation = new AgentInvocation
        {
            Id = Guid.NewGuid().ToString(),
            InboundEventId = request.InboundEventId,
            WorkItemId = request.WorkItemId,
            WorkItemRevision = request.WorkItemRevision,
            Profile = request.Profile,
            Provider = "Pending",
            Model = request.Configuration.Model,
            ReasoningEffort = request.Configuration.ReasoningEffort,
            RenderedPrompt = request.RenderedPrompt,
            AvailableTools = CreateToolCatalogSnapshot(request.AvailableTools),
            Status = AgentInvocationStatus.Running,
            StartedAt = timeProvider.GetUtcNow()
        };

        return repository.AddAsync(invocation, cancellationToken);
    }

    private static AgentInvocationToolCatalog? CreateToolCatalogSnapshot(ToolCatalogSnapshot? catalog) =>
        catalog is null
            ? null
            : new AgentInvocationToolCatalog
            {
                CatalogVersion = catalog.CatalogVersion,
                RawJson = catalog.RawJson,
                Tools = catalog.Tools.Select(tool => new AgentInvocationTool
                {
                    Id = tool.Id,
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchemaJson = tool.InputSchemaJson,
                    CanInvoke = tool.CanInvoke
                }).ToList()
            };

    public async Task CompleteAsync(
        string agentInvocationId,
        AgentReceipt receipt,
        CancellationToken cancellationToken)
    {
        var invocation = await repository.GetRequiredByIdAsync(agentInvocationId, cancellationToken);

        invocation.Provider = receipt.Provider;
        invocation.Model = receipt.Model;
        invocation.CompletionId = receipt.CompletionId;
        invocation.RawResponse = receipt.RawResponse;
        invocation.Status = AgentInvocationStatus.Succeeded;
        invocation.FinishedAt = timeProvider.GetUtcNow();

        await repository.UpdateAsync(invocation, cancellationToken);
    }

    public async Task FailAsync(
        string agentInvocationId,
        AgentInvocationStatus status,
        string error,
        CancellationToken cancellationToken)
    {
        if (status is not (AgentInvocationStatus.Failed or AgentInvocationStatus.OutcomeUnknown))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A failed invocation must use Failed or OutcomeUnknown status.");
        }

        var invocation = await repository.GetRequiredByIdAsync(agentInvocationId, cancellationToken);
        invocation.Status = status;
        invocation.Error = error;
        invocation.FinishedAt = timeProvider.GetUtcNow();

        await repository.UpdateAsync(invocation, cancellationToken);
    }
}
