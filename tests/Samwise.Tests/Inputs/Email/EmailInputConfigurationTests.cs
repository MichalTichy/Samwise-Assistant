using Samwise.Domain.Inputs.Email;

namespace Samwise.Tests.Inputs.Email;

public sealed class EmailInputConfigurationTests
{
    [Fact]
    public void UpdateNormalizesSortsAndDeduplicatesIgnoredSenders()
    {
        var configuration = new EmailInputConfiguration();

        configuration.Update(15, [" Rosie@Example.com ", "bilbo@example.com", "rosie@example.com"]);

        Assert.Equal(15, configuration.PollingIntervalMinutes);
        Assert.Equal(["bilbo@example.com", "rosie@example.com"], configuration.IgnoredSenderAddresses);
        Assert.True(configuration.IsIgnored("ROSIE@example.com"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void UpdateRejectsPollingIntervalOutsideOneDay(int interval)
    {
        var configuration = new EmailInputConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Update(interval, []));
    }
}
