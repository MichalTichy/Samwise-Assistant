using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Imap.Mailboxes;

internal static class CalendarInvitationParser
{
    public static EmailCalendarInvitation? Parse(string content, string? contentTypeMethod = null)
    {
        var properties = ParseProperties(content);
        if (!properties.Any(property => string.Equals(property.Name, "BEGIN", StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(property.Value, "VEVENT", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var start = Find(properties, "DTSTART");
        var end = Find(properties, "DTEND");
        return new EmailCalendarInvitation
        {
            Method = Value(properties, "METHOD") ?? contentTypeMethod?.Trim() ?? string.Empty,
            Uid = Value(properties, "UID") ?? string.Empty,
            Summary = Unescape(Value(properties, "SUMMARY")),
            Location = Unescape(Value(properties, "LOCATION")),
            Start = start?.Value ?? string.Empty,
            End = end?.Value ?? string.Empty,
            TimeZoneId = Parameter(start, "TZID") ?? Parameter(end, "TZID") ?? string.Empty,
            Organizer = FormatPerson(Find(properties, "ORGANIZER")),
            Attendees = properties
                .Where(property => string.Equals(property.Name, "ATTENDEE", StringComparison.OrdinalIgnoreCase))
                .Select(FormatPerson)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Status = Value(properties, "STATUS") ?? string.Empty
        };
    }

    private static List<CalendarProperty> ParseProperties(string content)
    {
        var unfolded = new List<string>();
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length > 0 && line[0] is ' ' or '\t' && unfolded.Count > 0)
            {
                unfolded[^1] += line[1..];
            }
            else
            {
                unfolded.Add(line.TrimEnd('\r'));
            }
        }

        var result = new List<CalendarProperty>();
        foreach (var line in unfolded)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var declaration = line[..separator];
            var parts = declaration.Split(';');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(1))
            {
                var equals = part.IndexOf('=');
                if (equals > 0)
                {
                    parameters[part[..equals]] = part[(equals + 1)..].Trim('"');
                }
            }

            result.Add(new CalendarProperty(parts[0], line[(separator + 1)..].Trim(), parameters));
        }

        return result;
    }

    private static CalendarProperty? Find(IEnumerable<CalendarProperty> properties, string name) =>
        properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string? Value(IEnumerable<CalendarProperty> properties, string name) =>
        Find(properties, name)?.Value;

    private static string? Parameter(CalendarProperty? property, string name) =>
        property is not null && property.Parameters.TryGetValue(name, out var value) ? value : null;

    private static string FormatPerson(CalendarProperty? property)
    {
        if (property is null)
        {
            return string.Empty;
        }

        var address = property.Value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? property.Value[7..]
            : property.Value;
        var name = Parameter(property, "CN");
        return string.IsNullOrWhiteSpace(name) ? address : $"{Unescape(name)} <{address}>";
    }

    private static string Unescape(string? value) => (value ?? string.Empty)
        .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("\\,", ",", StringComparison.Ordinal)
        .Replace("\\;", ";", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);

    private sealed record CalendarProperty(
        string Name,
        string Value,
        IReadOnlyDictionary<string, string> Parameters);
}
