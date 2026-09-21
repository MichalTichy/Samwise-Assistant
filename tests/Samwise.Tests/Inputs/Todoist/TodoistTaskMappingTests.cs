using System.Text.Json;
using Newtonsoft.Json;
using Samwise.Infrastructure.Todoist.Readers;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.InboundEvents;

namespace Samwise.Tests.Inputs.Todoist;

public sealed class TodoistTaskMappingTests
{
    [Theory]
    [InlineData("2026-09-21T15:00:00Z", null, null, "2026-09-21T15:00:00+00:00")]
    [InlineData("2026-09-21T15:00:00", "Europe/Prague", null, "2026-09-21T13:00:00+00:00")]
    [InlineData("2026-09-21T15:00:00", null, "Europe/Prague", "2026-09-21T13:00:00+00:00")]
    [InlineData("2026-09-21", null, "Europe/Prague", "2026-09-20T22:00:00+00:00")]
    [InlineData("2026-09-21", null, null, "2026-09-21T00:00:00+00:00")]
    public void DueResolvesToTheExpectedInstant(string date, string? timezone, string? fallback, string expected)
    {
        var due = new TodoistDue { Date = date, Timezone = timezone };

        Assert.Equal(DateTimeOffset.Parse(expected), due.ResolveDueAt(fallback));
    }

    [Fact]
    public void UnparseableDueIsIgnored()
    {
        Assert.Null(new TodoistDue { Date = "someday" }.ResolveDueAt(null));
    }

    [Fact]
    public void ReaderMapsAnActiveTaskWithDueAndDeadline()
    {
        using var document = JsonDocument.Parse("""
            {
              "id": "6X7rM8997g3RQmvh",
              "content": "Send the revised offer",
              "description": "Use the new price list",
              "project_id": "p-1",
              "labels": ["work", "waiting"],
              "priority": 4,
              "checked": false,
              "is_deleted": false,
              "due": { "date": "2026-09-22T09:00:00", "timezone": "Europe/Prague", "string": "tomorrow at 9", "is_recurring": false, "lang": "en" },
              "deadline": { "date": "2026-09-25", "lang": "en" }
            }
            """);

        var task = TodoistRestTaskReader.MapTask(
            document.RootElement,
            new Dictionary<string, string> { ["p-1"] = "Work" });

        Assert.NotNull(task);
        Assert.Equal("6X7rM8997g3RQmvh", task.TaskId);
        Assert.Equal("Work", task.ProjectName);
        Assert.Equal(["work", "waiting"], task.Labels);
        Assert.Equal(4, task.Priority);
        Assert.Equal("2026-09-22T09:00:00", task.Due.Date);
        Assert.Equal("Europe/Prague", task.Due.Timezone);
        Assert.Equal("tomorrow at 9", task.Due.Text);
        Assert.Equal(new DateOnly(2026, 9, 25), task.DeadlineDate);
        Assert.Equal("https://app.todoist.com/app/task/6X7rM8997g3RQmvh", task.Url);
    }

    [Theory]
    [InlineData("""{ "id": "1", "content": "No date", "due": null }""")]
    [InlineData("""{ "id": "2", "content": "Done", "checked": true, "due": { "date": "2026-09-22" } }""")]
    [InlineData("""{ "id": "3", "content": "Deleted", "is_deleted": true, "due": { "date": "2026-09-22" } }""")]
    public void ReaderSkipsTasksThatCannotBecomeDueInput(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Null(TodoistRestTaskReader.MapTask(document.RootElement, new Dictionary<string, string>()));
    }

    [Fact]
    public void ReaderDropsAnUnreadableDeadline()
    {
        using var document = JsonDocument.Parse("""
            { "id": "1", "content": "Odd", "due": { "date": "2026-09-22" }, "deadline": { "date": "soon" } }
            """);

        var task = TodoistRestTaskReader.MapTask(document.RootElement, new Dictionary<string, string>());

        Assert.NotNull(task);
        Assert.Null(task.DeadlineDate);
    }

    [Fact]
    public void ReaderReadsTheAccountTimezone()
    {
        using var document = JsonDocument.Parse("""{ "full_name": "Sam", "tz_info": { "timezone": "Europe/Prague", "hours": 2 } }""");

        Assert.Equal("Europe/Prague", TodoistRestTaskReader.ReadTimeZone(document.RootElement));
    }

    [Fact]
    public void InboundEventRoundTripsThroughDocumentSerialization()
    {
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };
        var inboundEvent = new TodoistTaskInboundEvent
        {
            TaskId = "task-1",
            Content = "Call Rosie",
            Due = new TodoistDue { Date = "2026-09-22", Text = "tomorrow", IsRecurring = true },
            DeadlineDate = new DateOnly(2026, 9, 25),
            Comments = [new TodoistTaskComment { CommentId = "c-1", Content = "Ask about Friday" }]
        };

        var json = JsonConvert.SerializeObject(inboundEvent, settings);
        var roundTrip = JsonConvert.DeserializeObject<TodoistTaskInboundEvent>(json, settings);

        Assert.NotNull(roundTrip);
        Assert.Equal(inboundEvent.ExternalId, roundTrip.ExternalId);
        Assert.True(roundTrip.Due.IsRecurring);
        Assert.Equal(new DateOnly(2026, 9, 25), roundTrip.DeadlineDate);
        Assert.Contains("Deadline: 2026-09-25", roundTrip.GetIntakeText());
        Assert.Equal("Ask about Friday", Assert.Single(roundTrip.Comments).Content);
    }

    [Theory]
    [InlineData(0, 24, 7)]
    [InlineData(15, -1, 7)]
    [InlineData(15, 24, 366)]
    public void ConfigurationRejectsOutOfRangeValues(int polling, int lead, int overdue)
    {
        var configuration = new TodoistInputConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            configuration.Update("token", polling, lead, overdue, null, [], []));
    }
}
