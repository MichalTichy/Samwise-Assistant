using JasperFx;
using Marten;
using Samwise.Domain.Agents;
using Samwise.Domain.WorkItems;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Tests.Persistence;

public sealed class WorkItemDocumentPersistenceTests
{
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

        firstCopy.AddExecutionUpdate("Execution completed.", now.AddMinutes(5));
        await repository.UpdateAsync(firstCopy);

        staleCopy.AddExecutionUpdate("Stale update.", now.AddMinutes(6));
        await Assert.ThrowsAsync<ConcurrencyException>(() => repository.UpdateAsync(staleCopy));

        var persisted = await repository.GetRequiredByIdAsync(workItem.Id);
        Assert.Equal(2, persisted.Comments.Count);
        var persistedPlan = Assert.Single(persisted.Plans);
        Assert.Equal(PlanReviewKind.Approved, Assert.IsType<PlanReview>(persistedPlan.Review).Kind);
        Assert.Equal("offer-preparation", Assert.IsType<PlanPlaybookSnapshot>(persistedPlan.Playbook).Id);
        Assert.Equal("sha256:test", persistedPlan.ExecutionTools.CatalogVersion);
        Assert.Equal("documents/create", Assert.Single(persistedPlan.ExecutionTools.Tools).Id);
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
