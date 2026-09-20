using Newtonsoft.Json;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Logic.Inbox.Events;
using Samwise.Logic.Inputs.Email.Events;

namespace Samwise.Tests.Persistence;

public sealed class InboundEventTypeOwnershipMigrationTests
{
    [Theory]
    [InlineData(
        "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "Samwise.Logic.Inbox.Events.ManualInboundEvent, Samwise.Logic")]
    [InlineData(
        "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
        "Samwise.Logic.Inputs.Email.Events.EmailInboundEvent, Samwise.Logic")]
    [InlineData(
        "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain",
        "Samwise.Logic.Inputs.Email.Events.SentEmailInboundEvent, Samwise.Logic")]
    public void DocumentTypeIsMovedToItsSourceFeature(string oldName, string expected)
    {
        Assert.Equal(expected, MigrateInboundEventTypeOwnership.RewriteDocumentType(oldName));
    }

    [Fact]
    public void PersistedJsonCanBeDeserializedAfterTypeNamesAreMigrated()
    {
        var oldJson = """
            {
              "$type": "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
              "Id": "event-1",
              "ReceivedAt": "2026-09-20T10:00:00+00:00",
              "Attachments": {
                "$type": "System.Collections.Generic.List`1[[Samwise.Domain.Inbox.EmailAttachmentSummary, Samwise.Domain]], System.Private.CoreLib",
                "$values": [
                  {
                    "$type": "Samwise.Domain.Inbox.EmailAttachmentSummary, Samwise.Domain",
                    "FileName": "invoice.pdf",
                    "ContentType": "application/pdf",
                    "Size": 42
                  }
                ]
              }
            }
            """;

        var migratedJson = MigrateInboundEventTypeOwnership.RewriteTypeNames(oldJson);
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };

        var inboundEvent = JsonConvert.DeserializeObject<InboundEvent>(migratedJson, settings);

        var email = Assert.IsType<EmailInboundEvent>(inboundEvent);
        Assert.Single(email.Attachments);
        Assert.Equal("invoice.pdf", email.Attachments[0].FileName);
        Assert.DoesNotContain("Samwise.Domain.Inbox", migratedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AllRootEventTypesAreRewrittenInsideJson()
    {
        var persistedNames = string.Join('|',
            "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain",
            "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
            "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain");

        var migratedNames = MigrateInboundEventTypeOwnership.RewriteTypeNames(persistedNames);

        Assert.Contains(typeof(ManualInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.Contains(typeof(EmailInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.Contains(typeof(SentEmailInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.DoesNotContain("Samwise.Domain.Inbox", migratedNames, StringComparison.Ordinal);
    }
}
