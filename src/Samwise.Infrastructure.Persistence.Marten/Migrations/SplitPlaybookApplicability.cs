using System.Data;
using System.Text.Json.Nodes;
using Marten;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Infrastructure.Persistence.Marten.Migrations;

public sealed class SplitPlaybookApplicability : IMartenMigration
{
    public int Version => 2026092301;

    public async Task MigrateAsync(IDocumentSession session, CancellationToken ct)
    {
        var playbooks = new List<(string Id, JsonObject Data)>();
        await using (var command = session.GetConnection().CreateCommand())
        {
            command.CommandText = "select id, data::text from samwise.mt_doc_playbook where not (data ? 'Applicability')";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var data = JsonNode.Parse(reader.GetString(1))?.AsObject()
                    ?? throw new DataException("A stored Playbook document is not valid JSON.");
                playbooks.Add((reader.GetString(0), data));
            }
        }

        foreach (var (id, data) in playbooks)
        {
            var description = data["Description"]?.GetValue<string>() ?? string.Empty;
            data["Applicability"] = ExtractApplicability(description);
            data["Description"] = RemoveApplicabilitySection(description);
            await using var command = session.GetConnection().CreateCommand();
            command.CommandText = "update samwise.mt_doc_playbook set data = @data::jsonb where id = @id";

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = id;
            command.Parameters.Add(idParameter);

            var dataParameter = command.CreateParameter();
            dataParameter.ParameterName = "@data";
            dataParameter.Value = data.ToJsonString();
            command.Parameters.Add(dataParameter);

            await command.ExecuteNonQueryAsync(ct);
        }
    }

    internal static string ExtractApplicability(string description)
    {
        var lines = Lines(description);
        var sectionEnd = FindApplicabilitySectionEnd(lines);
        var contentStart = sectionEnd is null ? 0 : 1;
        var contentEnd = sectionEnd ?? lines.Length;
        var applicability = string.Join('\n', lines[contentStart..contentEnd]).Trim();
        if (applicability.Length == 0)
        {
            applicability = description.Trim();
        }

        return applicability.Length <= 1_000 ? applicability : applicability[..1_000].TrimEnd();
    }

    internal static string RemoveApplicabilitySection(string description)
    {
        var lines = Lines(description);
        var sectionEnd = FindApplicabilitySectionEnd(lines);
        if (sectionEnd is null || sectionEnd.Value == lines.Length)
        {
            return description;
        }

        return string.Join('\n', lines[sectionEnd.Value..]).Trim();
    }

    private static string[] Lines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static int? FindApplicabilitySectionEnd(string[] lines)
    {
        if (lines.Length == 0 ||
            !string.Equals(lines[0].Trim(), "APPLICABILITY", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var index = 2; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index - 1]) && IsHeading(lines[index]))
            {
                return index;
            }
        }

        return lines.Length;
    }

    private static bool IsHeading(string value)
    {
        var heading = value.Trim();
        return heading.Length is > 0 and <= 80 &&
               heading.Any(char.IsLetter) &&
               heading.Where(char.IsLetter).All(char.IsUpper);
    }
}
