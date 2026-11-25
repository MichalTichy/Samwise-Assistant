using Marten;
using Shared.Infrastructure.Persistence.Marten.Migration;
using static Samwise.Infrastructure.Persistence.Marten.Migrations.MigrateInputModuleTypeOwnership;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class MigrateTodoistTaskSourceTypeOwnership : IMartenMigration
{
    internal static readonly TypeNameReplacement[] ConfigurationReplacements =
    [
        new("Samwise.Inputs.Todoist.Configuration.TodoistInputConfiguration, Samwise.Inputs.Todoist",
            "Samwise.TaskSources.Todoist.Configuration.TodoistTaskSourceConfiguration, Samwise.TaskSources.Todoist", true),
        new("Samwise.Inputs.Todoist.Configuration.TodoistInputSource, Samwise.Inputs.Todoist",
            "Samwise.TaskSources.Todoist.Configuration.TodoistSourceSelection, Samwise.TaskSources.Todoist", false)
    ];

    internal static readonly TypeNameReplacement[] LegacyEventReplacements =
    [
        new("Samwise.Inputs.Todoist.InboundEvents.TodoistTaskInboundEvent, Samwise.Inputs.Todoist",
            "Samwise.TaskSources.Todoist.Legacy.TodoistTaskInboundEvent, Samwise.TaskSources.Todoist", true),
        new("Samwise.Inputs.Todoist.InboundEvents.TodoistTaskComment, Samwise.Inputs.Todoist",
            "Samwise.TaskSources.Todoist.Legacy.TodoistTaskComment, Samwise.TaskSources.Todoist", false),
        new("Samwise.Inputs.Todoist.InboundEvents.TodoistDue, Samwise.Inputs.Todoist",
            "Samwise.TaskSources.Todoist.Models.TodoistDue, Samwise.TaskSources.Todoist", false)
    ];

    public int Version => 2026112501;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        await MigrateTableAsync(session, "samwise.mt_doc_todoistinputconfiguration", ConfigurationReplacements, ct);
        await MigrateTableAsync(session, "samwise.mt_doc_inboundevent", LegacyEventReplacements, ct);
    }
}
