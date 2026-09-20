using Marten;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class MigrateInboundEventsToMultipleOutcomes : IMartenMigration
{
    public int Version => 2026092001;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        await using var command = session.GetConnection().CreateCommand();
        command.CommandText = """
            update samwise.mt_doc_inboundevent
            set data = ((
                data
                || jsonb_build_object(
                    'Status',
                    case data ->> 'Handling'
                        when 'Pending' then 'Pending'
                        when 'NeedsReview' then 'NeedsReview'
                        else 'Processed'
                    end)
                || jsonb_build_object(
                    'Outcomes',
                    case
                        when data ->> 'Handling' in ('CreateWorkItem', 'AppendToWorkItem')
                             and nullif(data ->> 'WorkItemId', '') is not null
                        then jsonb_build_array(jsonb_build_object(
                            'Id', 'legacy',
                            'Kind', case data ->> 'Handling'
                                when 'CreateWorkItem' then 'CreatedWorkItem'
                                else 'UpdatedWorkItem'
                            end,
                            'WorkItemId', data ->> 'WorkItemId',
                            'RelevantText', coalesce(
                                nullif(data ->> 'Input', ''),
                                nullif(data ->> 'BodyText', ''),
                                nullif(data ->> 'Subject', ''),
                                'Previously processed input'),
                            'Explanation', coalesce(data ->> 'Explanation', '')))
                        else '[]'::jsonb
                    end))
                - 'Handling')
                - 'WorkItemId'
            where data ? 'Handling'
              and not data ? 'Status';
            """;

        await command.ExecuteNonQueryAsync(ct);
    }
}
