using JasperFx;
using Marten;
using Marten.Services;
using Newtonsoft.Json;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;
using Weasel.Core;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Tests.Persistence;

public sealed class WorkItemDocumentPersistenceTests
{
    [Fact]
    public async Task EmailAccountStoresNestedMailboxCheckpoints()
    {
        var connectionString = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var store = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            new SamwiseStorageConfiguration().Configure(options);
        });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        IRepository<EmailAccount> repository = new NoTenancyMartenRepository<EmailAccount>(
            new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));
        var now = DateTimeOffset.UtcNow;
        var account = new EmailAccount
        {
            Id = $"test-{Guid.NewGuid():N}",
            DisplayName = "Personal",
            EmailAddress = "sam@example.com",
            Host = "imap.example.com",
            Port = 993,
            Username = "sam@example.com",
            ProtectedPassword = "protected",
            InboxCheckpoint = new EmailMailboxCheckpoint(42, 173, now),
            SentCheckpoint = new EmailMailboxCheckpoint(43, 271, now),
            CreatedAt = now,
            UpdatedAt = now
        };

        await repository.AddAsync(account);

        var persisted = await repository.GetRequiredByIdAsync(account.Id);
        Assert.Equal(42u, persisted.InboxCheckpoint?.UidValidity);
        Assert.Equal(173u, persisted.InboxCheckpoint?.NextUid);
        Assert.Equal(43u, persisted.SentCheckpoint?.UidValidity);
        Assert.Equal(271u, persisted.SentCheckpoint?.NextUid);

        await repository.DeleteByIdAsync(account.Id);
    }

    [Fact]
    public async Task InboundEventHierarchyIsStoredInOneDocumentCollection()
    {
        var connectionString = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var store = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            var serializer = new JsonNetSerializer
            {
                EnumStorage = EnumStorage.AsString
            };
            serializer.Configure(settings => settings.TypeNameHandling = TypeNameHandling.Objects);
            options.Serializer(serializer);
            new SamwiseStorageConfiguration().Configure(options);
        });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        IRepository<InboundEvent> repository = new NoTenancyMartenRepository<InboundEvent>(
            new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));

        var manual = new ManualInboundEvent
        {
            Id = $"test-{Guid.NewGuid():N}",
            ManualInputId = Guid.NewGuid().ToString("N"),
            Input = "Bring potatoes.",
            DedupeKey = Guid.NewGuid().ToString("N"),
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var email = new EmailInboundEvent
        {
            Id = $"test-{Guid.NewGuid():N}",
            EmailAccountId = "personal",
            AccountName = "Personal",
            AccountEmailAddress = "sam@example.com",
            Mailbox = "INBOX",
            UidValidity = 7,
            Uid = 11,
            Subject = "Supper",
            FromAddress = "rosie@example.com",
            BodyText = "Come home.",
            WasReadWhenCaptured = true,
            WasAnsweredWhenCaptured = true,
            Replies =
            [
                new EmailReplySnapshot(
                    "reply-1",
                    "Re: Supper",
                    "sam@example.com",
                    "Sam",
                    ["rosie@example.com"],
                    DateTimeOffset.UtcNow,
                    "On my way.")
            ],
            DedupeKey = Guid.NewGuid().ToString("N"),
            ReceivedAt = DateTimeOffset.UtcNow
        };
        var sentEmail = new SentEmailInboundEvent
        {
            Id = $"test-{Guid.NewGuid():N}",
            EmailAccountId = "personal",
            AccountName = "Personal",
            AccountEmailAddress = "sam@example.com",
            Mailbox = "Sent",
            UidValidity = 7,
            Uid = 12,
            Subject = "Re: Supper",
            FromAddress = "sam@example.com",
            ToAddresses = ["rosie@example.com"],
            BodyText = "On my way.",
            DedupeKey = Guid.NewGuid().ToString("N"),
            ReceivedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync([manual, email, sentEmail]);

        Assert.IsType<ManualInboundEvent>(await repository.GetRequiredByIdAsync(manual.Id));
        var storedEmail = Assert.IsType<EmailInboundEvent>(
            await repository.GetRequiredByIdAsync(email.Id));
        Assert.Contains("Subject: Supper", storedEmail.GetIntakeText(), StringComparison.Ordinal);
        Assert.Equal("On my way.", Assert.Single(storedEmail.Replies).BodyText);
        var storedSentEmail = Assert.IsType<SentEmailInboundEvent>(
            await repository.GetRequiredByIdAsync(sentEmail.Id));
        Assert.Contains("Email sent from: sam@example.com", storedSentEmail.GetIntakeText(), StringComparison.Ordinal);

        await repository.DeleteRangeByIdsAsync([manual.Id, email.Id, sentEmail.Id]);
    }

    [Fact]
    public async Task AgentStagesAreStoredInsideOneConfigurationDocument()
    {
        var connectionString = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var store = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            new SamwiseStorageConfiguration().Configure(options);
        });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        IRepository<AgentConfiguration> repository = new NoTenancyMartenRepository<AgentConfiguration>(
            new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));

        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var configuration = new AgentConfiguration
        {
            Id = $"test-{Guid.NewGuid():N}",
            Intake = CreateStage("intake-model", "medium", now),
            Planning = CreateStage("planning-model", "high", now),
            Execution = CreateStage("execution-model", "max", now)
        };

        await repository.AddAsync(configuration);

        var persisted = await repository.GetRequiredByIdAsync(configuration.Id);
        Assert.Equal("intake-model", persisted.Intake.Model);
        Assert.Equal("planning-model", persisted.Planning.Model);
        Assert.Equal("execution-model", persisted.Execution.Model);
        Assert.Equal("max", persisted.Execution.ReasoningEffort);

        await repository.DeleteByIdAsync(configuration.Id);
    }

    [Fact]
    public async Task WorkItemGraphIsStoredAsOneRevisionedDocument()
    {
        var connectionString = Environment.GetEnvironmentVariable("SAMWISE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var store = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            new SamwiseStorageConfiguration().Configure(options);
        });
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        IRepository<DomainWorkItem> repository = new NoTenancyMartenRepository<DomainWorkItem>(
            new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));

        var now = new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);
        var workItem = DomainWorkItem.CreateFromInbound("Prepare the revised offer.", null, now);
        workItem.AddContext("Use the approved price list.", now.AddMinutes(1));
        var plan = new WorkItemPlan
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = workItem.Id,
            WorkItemRevision = workItem.Revision,
            PlanNumber = workItem.ReservePlanNumber(now.AddMinutes(2)),
            Summary = "Prepare and verify the offer.",
            Steps =
            [
                new PlanStep
                {
                    Order = 1,
                    Title = "Create the offer",
                    TechnicalDescription = "Create the approved offer document.",
                    ToolCalls =
                    [
                        new PlanToolCall
                        {
                            ToolId = "documents/create",
                            ArgumentsJson = "{\"title\":\"Revised offer\"}"
                        }
                    ],
                    HasExternalSideEffects = true,
                    SuccessCriteria = "The document ID is returned."
                }
            ],
            Playbook = new PlanPlaybookSnapshot
            {
                Id = "offer-preparation",
                Name = "Offer preparation",
                Description = "Prepare a customer offer and verify every supplied constraint.",
                UpdatedAt = now
            },
            ExecutionTools = new PlanToolCatalogSnapshot
            {
                CatalogVersion = "sha256:test",
                RawJson = "{\"catalog_version\":\"sha256:test\"}",
                Tools =
                [
                    new PlanToolCapability
                    {
                        Id = "documents/create",
                        Name = "create",
                        InputSchemaJson = "{\"type\":\"object\"}",
                        CanInvoke = true
                    }
                ]
            },
            CreatedAt = now.AddMinutes(3)
        };
        workItem.ApplyPlan(plan, now.AddMinutes(3));
        workItem.Approve(plan.Id, workItem.Revision, now.AddMinutes(4));

        await repository.AddAsync(workItem);

        var firstCopy = await repository.GetRequiredByIdAsync(workItem.Id);
        var staleCopy = await repository.GetRequiredByIdAsync(workItem.Id);

        firstCopy.StartExecution(plan.Id, firstCopy.Revision, now.AddMinutes(5));
        firstCopy.CompleteExecution(
            "Execution completed.",
            ["Created the revised offer document."],
            "execution-invocation-id",
            now.AddMinutes(6));
        await repository.UpdateAsync(firstCopy);

        staleCopy.AddExecutionUpdate("Stale update.", now.AddMinutes(7));
        await Assert.ThrowsAsync<ConcurrencyException>(() => repository.UpdateAsync(staleCopy));

        var persisted = await repository.GetRequiredByIdAsync(workItem.Id);
        Assert.Single(persisted.Comments);
        var completion = Assert.IsType<WorkItemCompletion>(persisted.Completion);
        Assert.Equal("Execution completed.", completion.Summary);
        Assert.Equal("Created the revised offer document.", Assert.Single(completion.Outputs));
        Assert.Equal("execution-invocation-id", completion.AgentInvocationId);
        var persistedPlan = Assert.Single(persisted.Plans);
        Assert.Equal(PlanReviewKind.Approved, Assert.IsType<PlanReview>(persistedPlan.Review).Kind);
        Assert.Equal("offer-preparation", Assert.IsType<PlanPlaybookSnapshot>(persistedPlan.Playbook).Id);
        Assert.Equal("sha256:test", persistedPlan.ExecutionTools.CatalogVersion);
        Assert.Equal("documents/create", Assert.Single(persistedPlan.ExecutionTools.Tools).Id);
        var persistedToolCall = Assert.Single(Assert.Single(persistedPlan.Steps).ToolCalls);
        Assert.Equal("documents/create", persistedToolCall.ToolId);
        Assert.Equal("{\"title\":\"Revised offer\"}", persistedToolCall.ArgumentsJson);
        Assert.DoesNotContain(persisted.Comments, comment => comment.Body == "Stale update.");
        Assert.True(persisted.Version > workItem.Version);

        await repository.DeleteByIdAsync(workItem.Id);
    }

    private static AgentStageConfiguration CreateStage(
        string model,
        string reasoningEffort,
        DateTimeOffset updatedAt)
        => new()
        {
            Prompt = $"Prompt for {model}",
            Model = model,
            ReasoningEffort = reasoningEffort,
            UpdatedAt = updatedAt
        };
}
