using Marten;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class MigrateInboundEventTypeOwnership : IMartenMigration
{
    private static readonly TypeNameReplacement[] Replacements =
    [
        new(
            "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain",
            "Samwise.Logic.Inbox.Events.ManualInboundEvent, Samwise.Logic",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
            "Samwise.Logic.Inputs.Email.Events.EmailInboundEvent, Samwise.Logic",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain",
            "Samwise.Logic.Inputs.Email.Events.SentEmailInboundEvent, Samwise.Logic",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.EmailAttachmentSummary, Samwise.Domain",
            "Samwise.Logic.Inputs.Email.EmailAttachmentSummary, Samwise.Logic",
            IsDocumentType: false)
    ];

    public int Version => 2026092002;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        await using var command = session.GetConnection().CreateCommand();

        var rewrittenData = "data::text";
        var dataPredicates = new List<string>();
        var documentTypeCases = new List<string>();

        for (var index = 0; index < Replacements.Length; index++)
        {
            var replacement = Replacements[index];
            var oldNameParameter = $"@old_name_{index}";
            var newNameParameter = $"@new_name_{index}";

            rewrittenData = $"replace({rewrittenData}, {oldNameParameter}, {newNameParameter})";
            dataPredicates.Add($"position({oldNameParameter} in data::text) > 0");

            if (replacement.IsDocumentType)
            {
                documentTypeCases.Add(
                    $"when mt_dotnet_type like {oldNameParameter} || '%' then {newNameParameter}");
            }

            var oldName = command.CreateParameter();
            oldName.ParameterName = oldNameParameter;
            oldName.Value = replacement.OldName;
            command.Parameters.Add(oldName);

            var newName = command.CreateParameter();
            newName.ParameterName = newNameParameter;
            newName.Value = replacement.NewName;
            command.Parameters.Add(newName);
        }

        command.CommandText = $$"""
            update samwise.mt_doc_inboundevent
            set data = ({{rewrittenData}})::jsonb,
                mt_dotnet_type = case
                    {{string.Join(Environment.NewLine + "                    ", documentTypeCases)}}
                    else mt_dotnet_type
                end
            where {{string.Join(Environment.NewLine + "               or ", dataPredicates)}}
               or {{string.Join(Environment.NewLine + "               or ", Replacements
                   .Select((replacement, index) => (replacement, index))
                   .Where(item => item.replacement.IsDocumentType)
                   .Select(item => $"mt_dotnet_type like @old_name_{item.index} || '%'"))}};
            """;

        await command.ExecuteNonQueryAsync(ct);
    }

    internal static string RewriteTypeNames(string value)
    {
        foreach (var replacement in Replacements)
        {
            value = value.Replace(replacement.OldName, replacement.NewName, StringComparison.Ordinal);
        }

        return value;
    }

    internal static string RewriteDocumentType(string value)
    {
        var replacement = Replacements.FirstOrDefault(candidate =>
            candidate.IsDocumentType && value.StartsWith(candidate.OldName, StringComparison.Ordinal));

        return replacement is null ? value : replacement.NewName;
    }

    private sealed record TypeNameReplacement(string OldName, string NewName, bool IsDocumentType);
}
