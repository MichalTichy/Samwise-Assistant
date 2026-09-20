using Newtonsoft.Json;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Inputs.Email;
using Samwise.Inputs.Email.Events;
using Samwise.Inputs.Manual.Events;

namespace Samwise.Tests.Persistence;

public sealed class InputModuleTypeOwnershipMigrationTests
{
    [Theory]
    [InlineData(
        "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
        "Samwise.Inputs.Manual.Events.ManualInboundEvent, Samwise.Inputs.Manual")]
    [InlineData(
        "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
        "Samwise.Inputs.Email.Events.EmailInboundEvent, Samwise.Inputs.Email")]
    [InlineData(
        "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain",
        "Samwise.Inputs.Email.Events.SentEmailInboundEvent, Samwise.Inputs.Email")]
    public void InboundDocumentTypeIsMovedToItsSourceModule(string oldName, string expected)
    {
        Assert.Equal(
            expected,
            MigrateInputModuleTypeOwnership.RewriteDocumentType(
                oldName,
                MigrateInputModuleTypeOwnership.InboundEventReplacements));
    }

    [Fact]
    public void PersistedInboundJsonCanBeDeserializedAfterTypeNamesAreMigrated()
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

        var migratedJson = MigrateInputModuleTypeOwnership.RewriteTypeNames(
            oldJson,
            MigrateInputModuleTypeOwnership.InboundEventReplacements);
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };

        var inboundEvent = JsonConvert.DeserializeObject<InboundEvent>(migratedJson, settings);

        var email = Assert.IsType<EmailInboundEvent>(inboundEvent);
        Assert.Single(email.Attachments);
        Assert.Equal("invoice.pdf", email.Attachments[0].FileName);
        Assert.DoesNotContain("Samwise.Domain.Inbox", migratedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedEmailAccountCanBeDeserializedAfterTypeNamesAreMigrated()
    {
        var oldJson = """
            {
              "$type": "Samwise.Domain.Inputs.Email.EmailAccount, Samwise.Domain",
              "Id": "account-1",
              "InboxCheckpoint": {
                "$type": "Samwise.Domain.Inputs.Email.EmailInboxCheckpoint, Samwise.Domain",
                "UidValidity": 42,
                "NextUid": 173,
                "UpdatedAt": "2026-09-20T10:00:00+00:00"
              }
            }
            """;

        var migratedJson = MigrateInputModuleTypeOwnership.RewriteTypeNames(
            oldJson,
            MigrateInputModuleTypeOwnership.EmailAccountReplacements);
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };

        var account = JsonConvert.DeserializeObject<EmailAccount>(migratedJson, settings);

        Assert.NotNull(account);
        Assert.IsType<EmailInboxCheckpoint>(account.InboxCheckpoint);
        Assert.DoesNotContain("Samwise.Domain.Inputs.Email", migratedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AllRootInboundEventTypesAreRewrittenInsideJson()
    {
        var persistedNames = string.Join('|',
            "Samwise.Domain.Inbox.ManualInboundEvent, Samwise.Domain",
            "Samwise.Domain.Inbox.EmailInboundEvent, Samwise.Domain",
            "Samwise.Domain.Inbox.SentEmailInboundEvent, Samwise.Domain");

        var migratedNames = MigrateInputModuleTypeOwnership.RewriteTypeNames(
            persistedNames,
            MigrateInputModuleTypeOwnership.InboundEventReplacements);

        Assert.Contains(typeof(ManualInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.Contains(typeof(EmailInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.Contains(typeof(SentEmailInboundEvent).FullName!, migratedNames, StringComparison.Ordinal);
        Assert.DoesNotContain("Samwise.Domain.Inbox", migratedNames, StringComparison.Ordinal);
    }
}
