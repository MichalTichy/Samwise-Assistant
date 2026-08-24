using JasperFx;
using Marten;
using Samwise.Domain.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Tests.Persistence;

public sealed class TaskDocumentPersistenceTests
{
    [Fact]
    public async Task TaskGraphIsStoredAsOneRevisionedDocument()
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
        IRepository<DomainTask> repository = new NoTenancyMartenRepository<DomainTask>(
            new NoTenancyByDefaultSessionFactory(store, new CurrentTenancyProviderNoTenancy()));

        var now = new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);
        var task = DomainTask.CreateFromInbound("Prepare the revised offer.", null, now);
        task.AddContext("Use the approved price list.", now.AddMinutes(1));
        var plan = new TaskPlan
        {
            Id = Guid.NewGuid().ToString(),
            TaskId = task.Id,
            TaskRevision = task.Revision,
            PlanNumber = task.ReservePlanNumber(now.AddMinutes(2)),
            Summary = "Prepare and verify the offer.",
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
        task.ApplyPlan(plan, now.AddMinutes(3));
        task.Approve(plan.Id, task.Revision, now.AddMinutes(4));

        await repository.AddAsync(task);

        var firstCopy = await repository.GetRequiredByIdAsync(task.Id);
        var staleCopy = await repository.GetRequiredByIdAsync(task.Id);

        firstCopy.AddExecutionUpdate("Execution completed.", now.AddMinutes(5));
        await repository.UpdateAsync(firstCopy);

        staleCopy.AddExecutionUpdate("Stale update.", now.AddMinutes(6));
        await Assert.ThrowsAsync<ConcurrencyException>(() => repository.UpdateAsync(staleCopy));

        var persisted = await repository.GetRequiredByIdAsync(task.Id);
        Assert.Equal(2, persisted.Comments.Count);
        Assert.Single(persisted.Decisions);
        var persistedPlan = Assert.Single(persisted.Plans);
        Assert.Equal("sha256:test", persistedPlan.ExecutionTools.CatalogVersion);
        Assert.Equal("documents/create", Assert.Single(persistedPlan.ExecutionTools.Tools).Id);
        Assert.DoesNotContain(persisted.Comments, comment => comment.Body == "Stale update.");
        Assert.True(persisted.Version > task.Version);

        await repository.DeleteByIdAsync(task.Id);
    }
}
