using Marten;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Domain.Playbooks;
using Samwise.Domain.WorkItems;
using Shared.Infrastructure.Persistence.Marten.StorageConfiguration;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;

namespace Samwise.Infrastructure.Persistence.Marten.Storage;

internal sealed class SamwiseStorageConfiguration : IStorageConfiguration
{
    public void Configure(StoreOptions options)
    {
        options.DatabaseSchemaName = "samwise";

        // Keep the pre-rename document identity so existing databases need no table migration.
        options.Schema.For<DomainWorkItem>()
            .Index(workItem => workItem.Status);
        options.Schema.For<AgentInvocation>().Index(invocation => invocation.WorkItemId);
        options.Schema.For<InboundEvent>()
            .AddSubClass<ManualInboundEvent>()
            .AddSubClass<EmailInboundEvent>()
            .AddSubClass<SentEmailInboundEvent>()
            .Index(item => item.ReceivedAt)
            .UniqueIndex("ux_inbound_event_dedupe_key", item => item.DedupeKey);
        options.Schema.For<EmailInputConfiguration>();
        options.Schema.For<EmailAccount>().Index(account => account.IsEnabled);
        options.Schema.For<AgentConfiguration>();
        options.Schema.For<Playbook>().Index(playbook => playbook.IsEnabled);
    }
}
