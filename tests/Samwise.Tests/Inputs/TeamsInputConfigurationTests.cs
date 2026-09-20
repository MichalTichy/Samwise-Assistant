using Samwise.Inputs.Teams;

namespace Samwise.Tests.Inputs;

public sealed class TeamsInputConfigurationTests
{
    [Fact]
    public void UpdateNormalizesIgnoredSourcesAndFlags()
    {
        var configuration = new TeamsInputConfiguration();

        configuration.Update(
            " tenant-id ",
            " client-id ",
            "protected-secret",
            12,
            3,
            " Work Teams guidance. ",
            includeChatMessages: false,
            includeChannelMessages: true,
            includeCallTranscripts: true,
            [
                new TeamsInputSource { Id = " person-1 ", DisplayName = " Person one " },
                new TeamsInputSource { Id = "person-1", DisplayName = "Duplicate" }
            ],
            [new TeamsInputSource { Id = "chat-1", DisplayName = string.Empty }],
            [new TeamsInputSource { Id = "channel-1", DisplayName = "Announcements" }]);

        Assert.Equal(12, configuration.PollingIntervalMinutes);
        Assert.Equal("tenant-id", configuration.MicrosoftGraph.TenantId);
        Assert.Equal("client-id", configuration.MicrosoftGraph.ClientId);
        Assert.Equal("protected-secret", configuration.MicrosoftGraph.ProtectedClientSecret);
        Assert.True(configuration.MicrosoftGraph.HasClientSecret);
        Assert.Equal(3, configuration.DebounceMinutes);
        Assert.Equal("Work Teams guidance.", configuration.IntakeGuidance);
        Assert.False(configuration.IncludeChatMessages);
        Assert.True(configuration.IncludeChannelMessages);
        Assert.True(configuration.IncludeCallTranscripts);
        var person = Assert.Single(configuration.IgnoredPeople);
        Assert.Equal("person-1", person.Id);
        Assert.Equal("Person one", person.DisplayName);
        Assert.Equal("chat-1", Assert.Single(configuration.IgnoredChats).DisplayName);
        Assert.True(configuration.IsPersonIgnored("person-1"));
        Assert.True(configuration.IsChannelIgnored("channel-1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void UpdateRejectsInvalidPollingInterval(int minutes)
    {
        var configuration = new TeamsInputConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Update(
            minutes,
            2,
            null,
            true,
            true,
            false,
            [],
            [],
            []));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void UpdateRejectsInvalidDebounceInterval(int minutes)
    {
        var configuration = new TeamsInputConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Update(
            5,
            minutes,
            null,
            true,
            true,
            false,
            [],
            [],
            []));
    }
}
