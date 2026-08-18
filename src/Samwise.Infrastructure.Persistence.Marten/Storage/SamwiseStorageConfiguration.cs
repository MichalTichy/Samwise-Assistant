using Marten;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Prompts;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Inbox;
using Shared.Infrastructure.Persistence.Marten.StorageConfiguration;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Infrastructure.Persistence.Marten.Storage;

internal sealed class SamwiseStorageConfiguration : IStorageConfiguration
{
    public void Configure(StoreOptions options)
    {
        options.DatabaseSchemaName = "samwise";

        // Keep the pre-rename document identity so existing databases need no table migration.
        options.Schema.For<DomainTask>()
            .DocumentAlias("samwisetask")
            .UseOptimisticConcurrency(true)
            .Index(task => task.Status);
        options.Schema.For<TaskPlan>().Index(plan => plan.TaskId);
        options.Schema.For<TaskComment>().Index(comment => comment.TaskId);
        options.Schema.For<PlanDecision>().Index(decision => decision.TaskId);
        options.Schema.For<AgentInvocation>().Index(invocation => invocation.TaskId);
        options.Schema.For<AgentWorkItem>().Index(item => item.Status);
        options.Schema.For<InboundEvent>()
            .UseOptimisticConcurrency(true)
            .Index(item => item.ReceivedAt)
            .UniqueIndex(MartenInboundEventStore.DedupeIndexName, item => item.DedupeKey);
        options.Schema.For<PromptTemplate>();
    }
}
