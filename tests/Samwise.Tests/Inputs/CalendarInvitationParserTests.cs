using Samwise.Infrastructure.Email.Imap.Mailboxes;

namespace Samwise.Tests.Inputs;

public sealed class CalendarInvitationParserTests
{
    [Fact]
    public void ParsesMeetingMetadataAndUnfoldedAttendees()
    {
        const string calendar = """
            BEGIN:VCALENDAR
            METHOD:REQUEST
            BEGIN:VEVENT
            UID:meeting-123
            DTSTART;TZID=Europe/Prague:20261027T180000
            DTEND;TZID=Europe/Prague:20261027T190000
            SUMMARY:MeetUpdate Brno
            LOCATION:Impact Hub Brno
            ORGANIZER;CN=Jiří Novák:mailto:jiri@example.com
            ATTENDEE;CN=Michal Tichý:mailto:michal@example.com
            ATTENDEE;CN=Martin
             Nový:mailto:martin@example.com
            STATUS:CONFIRMED
            END:VEVENT
            END:VCALENDAR
            """;

        var result = CalendarInvitationParser.Parse(calendar);

        Assert.NotNull(result);
        Assert.Equal("REQUEST", result.Method);
        Assert.Equal("meeting-123", result.Uid);
        Assert.Equal("20261027T180000", result.Start);
        Assert.Equal("20261027T190000", result.End);
        Assert.Equal("Europe/Prague", result.TimeZoneId);
        Assert.Equal("Jiří Novák <jiri@example.com>", result.Organizer);
        Assert.Equal(["Michal Tichý <michal@example.com>", "MartinNový <martin@example.com>"], result.Attendees);
    }

    [Fact]
    public void IgnoresCalendarContentWithoutAnEvent()
    {
        Assert.Null(CalendarInvitationParser.Parse("BEGIN:VCALENDAR\nEND:VCALENDAR", "PUBLISH"));
    }
}
