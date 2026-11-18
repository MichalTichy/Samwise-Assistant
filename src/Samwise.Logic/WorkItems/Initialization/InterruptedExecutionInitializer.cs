using Samwise.Domain.Agents;
using Samwise.Domain.Tasks;
using Samwise.Domain.WorkItems;
using Samwise.Logic.WorkItems.Services;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.WorkItems.Initialization;

/// <summary>Samwise runs as one web process. Before it accepts work, old execution claims are uncertain.</summary>
public sealed class InterruptedExecutionInitializer(IRepository<WorkItem> workItems,
    IRepository<AgentInvocation> invocations, TimeProvider clock, IWorkItemEventPublisher events) : InitializerBase
{
    public override int Priority => -100;
    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;
    public override bool RunOnlyInLeaderInstance => true;

    protected override async Task RunInitializationLogicAsync()
    {
        foreach (var item in await workItems.ListAsync(new ExecutingWorkItemsSpecification()))
        {
            var running = await invocations.ListAsync(new RunningExecutionInvocationsSpecification(item.Id));
            item.MarkExecutionUncertain(clock.GetUtcNow(), TaskErrors.InterruptedExecution);
            item.AddExecutionUpdate(TaskErrors.InterruptedExecution, clock.GetUtcNow());
            foreach (var invocation in running)
            {
                invocation.Status = AgentInvocationStatus.OutcomeUnknown;
                invocation.Error = TaskErrors.InterruptedExecution;
                invocation.FinishedAt = clock.GetUtcNow();
            }
            await PersistAsync(item, running);
            await events.PublishAsync(item);
        }
    }

    private async Task PersistAsync(WorkItem item, IReadOnlyList<AgentInvocation> running)
    {
        await using var unit = new UnitOfWorkProvider();
        await workItems.UpdateAsync(item);
        foreach (var invocation in running) await invocations.UpdateAsync(invocation);
        await unit.CommitAsync();
    }
}
