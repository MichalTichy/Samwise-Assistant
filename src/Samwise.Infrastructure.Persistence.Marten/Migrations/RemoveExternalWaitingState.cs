using Marten;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class RemoveExternalWaitingState : IMartenMigration
{
    public int Version => 2026111701;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        // Read raw JSON: the removed enum name must never be deserialized into the domain model.
        var rows = await session.QueryAsync<string>(
            "select data::text from samwise.mt_doc_workitem where coalesce(data->>'Status', data->>'status') in ('AwaitingExternalEvent', '9')", ct);
        foreach (var row in rows)
        {
            var data = JObject.Parse(row);
            Rewrite(data);
            var id = data.GetValue("id", StringComparison.OrdinalIgnoreCase)!.Value<string>()!;
            session.QueueSqlCommand("update samwise.mt_doc_workitem set data = ?::jsonb where id = ?",
                data.ToString(Formatting.None), id);
        }
    }

    internal static void Rewrite(JObject data)
    {
        var status = data.Property("status", StringComparison.OrdinalIgnoreCase)!;
        // Current persistence stores enums as strings, including when upgrading an older numeric row.
        status.Value = new JValue("NeedsAttention");
        var waiting = data.Property("waitingReason", StringComparison.OrdinalIgnoreCase);
        var priorFeedback = data.GetValue("LatestPlanningFeedback", StringComparison.OrdinalIgnoreCase)?.Value<string>();
        Set("LatestPlanningFeedback", string.Join("\n\n", new[] { priorFeedback,
            Samwise.Domain.Tasks.TaskErrors.LegacyWaiting, waiting?.Value.Value<string>() }
            .Where(value => !string.IsNullOrWhiteSpace(value))));
        waiting?.Remove();
        Set("CurrentPlanId", null);
        Set("PendingPlanningQuestion", null);
        var revision = data.Property("revision", StringComparison.OrdinalIgnoreCase);
        if (revision is not null) revision.Value = revision.Value.Value<int>() + 1;
        void Set(string name, string? value)
        {
            var property = data.Property(name, StringComparison.OrdinalIgnoreCase);
            if (property is not null) property.Value = value is null ? JValue.CreateNull() : new JValue(value);
            else data[char.IsLower(status.Name[0]) ? char.ToLowerInvariant(name[0]) + name[1..] : name] = value;
        }
    }
}
