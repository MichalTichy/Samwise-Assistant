using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Installers;
using Samwise.Infrastructure.Persistence.Marten.Initialization;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Inputs.Email;
using Samwise.Inputs.Email.Events;
using Samwise.Inputs.Manual.Events;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Marten.Migration;

namespace Samwise.Tests.Persistence;

public sealed class InputModuleTypeOwnershipMigrationTests
{
    [Fact]
    public void PersistenceInstallerRegistersMigrationsAndRunsThemBeforeDocumentInitializers()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:samwise"] = "Host=localhost;Database=samwise;Username=test;Password=test"
        }).Build();
        new MartenInstaller().Install(services, configuration, isProduction: false);
        services.AddAllInitializers("Samwise");

        using var provider = services.BuildServiceProvider();
        var migrations = provider.GetServices<IMartenMigration>().ToList();

        Assert.Contains(migrations, migration => migration is MigrateInputModuleTypeOwnership);
        Assert.Contains(migrations, migration => migration is MigrateInboundEventsToMultipleOutcomes);
        Assert.Contains(migrations, migration => migration is SplitPlaybookApplicability);
        Assert.Contains(services, service => service.ServiceType == typeof(IInitializer)
            && service.ImplementationType == typeof(SamwiseDatabaseMigrationInitializer));
        var databaseInitializer = services.Single(service => service.ImplementationType ==
            typeof(SamwiseDatabaseMigrationInitializer));
        Assert.Equal(ServiceLifetime.Transient, databaseInitializer.Lifetime);
        var databasePriority = new SamwiseDatabaseMigrationInitializer(null!, null!).Priority;
        var emailPriority = new Samwise.Inputs.Email.Initialization.EmailInputConfigurationInitializer(null!).Priority;
        Assert.True(databasePriority > emailPriority);
    }

    [Fact]
    public void PlaybookApplicabilityIsExtractedFromTheFirstGuidanceSection()
    {
        const string description = """
            APPLICABILITY

            Use this playbook for recurring marketing messages with a safe unsubscribe method.

            Do not use it for transactional mail.

            REQUIRED INVESTIGATION

            Inspect the full source message.
            """;

        var applicability = SplitPlaybookApplicability.ExtractApplicability(description);
        var guidance = SplitPlaybookApplicability.RemoveApplicabilitySection(description);

        Assert.Equal(
            "Use this playbook for recurring marketing messages with a safe unsubscribe method.\n\nDo not use it for transactional mail.",
            applicability);
        Assert.Equal("REQUIRED INVESTIGATION\n\nInspect the full source message.", guidance);
    }

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
