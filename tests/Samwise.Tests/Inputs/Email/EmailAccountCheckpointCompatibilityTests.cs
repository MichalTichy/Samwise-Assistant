using Newtonsoft.Json;
using Samwise.Inputs.Email;

namespace Samwise.Tests.Inputs.Email;

public sealed class EmailAccountCheckpointCompatibilityTests
{
    [Fact]
    public void ExistingInboxCheckpointTypeCanStillBeDeserialized()
    {
        var account = new EmailAccount
        {
            Id = "account-1",
            InboxCheckpoint = new EmailInboxCheckpoint(42, 173, DateTimeOffset.UtcNow)
        };
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects
        };
        var json = JsonConvert.SerializeObject(account, settings);

        var deserialized = JsonConvert.DeserializeObject<EmailAccount>(json, settings);

        var checkpoint = Assert.IsType<EmailInboxCheckpoint>(deserialized?.InboxCheckpoint);
        Assert.Equal(42u, checkpoint.UidValidity);
        Assert.Equal(173u, checkpoint.NextUid);
    }
}
