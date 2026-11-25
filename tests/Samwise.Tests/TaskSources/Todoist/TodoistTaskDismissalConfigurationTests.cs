using Newtonsoft.Json;
using Samwise.TaskSources.Todoist.Configuration;

namespace Samwise.Tests.TaskSources.Todoist;

public sealed class TodoistTaskDismissalConfigurationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T09:00:00Z");

    [Fact]
    public void RepeatedDismissalKeepsOneEntryAndOriginalTimeUntilRestored()
    {
        var configuration = new TodoistTaskSourceConfiguration();

        configuration.DismissTask(" 42 ", "Water plants", "https://app.todoist.com/app/task/42", "todoist:42:0", Now);
        configuration.DismissTask("42", "Water plants weekly", "https://app.todoist.com/app/task/42", "todoist:42:1", Now.AddDays(1));

        var dismissed = Assert.Single(configuration.DismissedTasks);
        Assert.Equal("42", dismissed.TaskId);
        Assert.Equal("Water plants weekly", dismissed.Title);
        Assert.Equal("todoist:42:1", dismissed.AgentTaskId);
        Assert.Equal(Now, dismissed.DismissedAt);
        Assert.True(configuration.IsTaskDismissed("42"));
        Assert.False(configuration.IsTaskDismissed("4"));

        Assert.True(configuration.RestoreTask("42"));
        Assert.False(configuration.RestoreTask("42"));
        Assert.False(configuration.IsTaskDismissed("42"));
    }

    [Fact]
    public void SettingsSaveDoesNotClearDismissedTasks()
    {
        var configuration = new TodoistTaskSourceConfiguration();
        configuration.DismissTask("42", "Water plants", string.Empty, null, Now);

        configuration.Update(null, 15, 24, 7, null, [], []);

        Assert.True(configuration.IsTaskDismissed("42"));
    }

    [Fact]
    public void ConfigurationSavedBeforeTaskRemovalLoadsWithoutDismissedTasks()
    {
        const string legacyDocument = """{"Id":"todoist-input","ProtectedApiToken":"token","IgnoredProjects":[],"IgnoredLabels":[]}""";

        var configuration = JsonConvert.DeserializeObject<TodoistTaskSourceConfiguration>(legacyDocument)!;

        Assert.Empty(configuration.DismissedTasks);
        Assert.False(configuration.IsTaskDismissed("42"));
    }
}
