using Marten;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class MigrateInputModuleTypeOwnership : IMartenMigration
{
    internal static readonly TypeNameReplacement[] InboundEventReplacements =
    [
        new(
            "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain",
            "Samwise.Inputs.Manual.Events.ManualInboundEvent, Samwise.Inputs.Manual",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
            "Samwise.Inputs.Email.Events.EmailInboundEvent, Samwise.Inputs.Email",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain",
            "Samwise.Inputs.Email.Events.SentEmailInboundEvent, Samwise.Inputs.Email",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inbox.EmailAttachmentSummary, Samwise.Domain",
            "Samwise.Inputs.Email.EmailAttachmentSummary, Samwise.Inputs.Email",
            IsDocumentType: false)
    ];

    internal static readonly TypeNameReplacement[] EmailAccountReplacements =
    [
        new(
            "Samwise.Domain.Inputs.Email.EmailAccount, Samwise.Domain",
            "Samwise.Inputs.Email.EmailAccount, Samwise.Inputs.Email",
            IsDocumentType: true),
        new(
            "Samwise.Domain.Inputs.Email.EmailMailboxCheckpoint, Samwise.Domain",
            "Samwise.Inputs.Email.EmailMailboxCheckpoint, Samwise.Inputs.Email",
            IsDocumentType: false),
        new(
            "Samwise.Domain.Inputs.Email.EmailInboxCheckpoint, Samwise.Domain",
            "Samwise.Inputs.Email.EmailInboxCheckpoint, Samwise.Inputs.Email",
            IsDocumentType: false)
    ];

    internal static readonly TypeNameReplacement[] EmailConfigurationReplacements =
    [
        new(
            "Samwise.Domain.Inputs.Email.EmailInputConfiguration, Samwise.Domain",
            "Samwise.Inputs.Email.EmailInputConfiguration, Samwise.Inputs.Email",
            IsDocumentType: true)
    ];

    public int Version => 2026092002;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        await MigrateTableAsync(
            session,
            "samwise.mt_doc_inboundevent",
            InboundEventReplacements,
            ct);
        await MigrateTableAsync(
            session,
            "samwise.mt_doc_emailaccount",
            EmailAccountReplacements,
            ct);
        await MigrateTableAsync(
            session,
            "samwise.mt_doc_emailinputconfiguration",
            EmailConfigurationReplacements,
            ct);
    }

    internal static string RewriteTypeNames(string value, IEnumerable<TypeNameReplacement> replacements)
    {
        foreach (var replacement in replacements)
        {
            value = value.Replace(replacement.OldName, replacement.NewName, StringComparison.Ordinal);
        }

        return value;
    }

    internal static string RewriteDocumentType(string value, IEnumerable<TypeNameReplacement> replacements)
    {
        var replacement = replacements.FirstOrDefault(candidate =>
            candidate.IsDocumentType && value.StartsWith(candidate.OldName, StringComparison.Ordinal));

        return replacement is null ? value : replacement.NewName;
    }

    private static async Task MigrateTableAsync(
        IDocumentSession session,
        string tableName,
        IReadOnlyList<TypeNameReplacement> replacements,
        CancellationToken cancellationToken)
    {
        await using var command = session.GetConnection().CreateCommand();
        var rewrittenData = "data::text";
        var dataPredicates = new List<string>();
        var documentTypeCases = new List<string>();

        for (var index = 0; index < replacements.Count; index++)
        {
            var replacement = replacements[index];
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

        var documentTypePredicates = replacements
            .Select((replacement, index) => (replacement, index))
            .Where(item => item.replacement.IsDocumentType)
            .Select(item => $"mt_dotnet_type like @old_name_{item.index} || '%'")
            .ToList();
        command.CommandText = $$"""
            update {{tableName}}
            set data = ({{rewrittenData}})::jsonb,
                mt_dotnet_type = case
                    {{string.Join(Environment.NewLine + "                    ", documentTypeCases)}}
                    else mt_dotnet_type
                end
            where {{string.Join(Environment.NewLine + "               or ", dataPredicates.Concat(documentTypePredicates))}};
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal sealed record TypeNameReplacement(string OldName, string NewName, bool IsDocumentType);
}
