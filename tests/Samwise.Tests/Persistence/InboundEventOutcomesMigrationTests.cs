using JasperFx;
using Marten;
using Marten.Services;
using Newtonsoft.Json;
using Npgsql;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Migrations;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Marten.UnitOfWorks;
using Shared.Infrastructure.Persistence.Repositories;
using Weasel.Core;

namespace Samwise.Tests.Persistence;

public sealed class InboundEventOutcomesMigrationTests
{
    [Fact]
    public void MigrationHasAStableVersion()
    {
        Assert.Equal(2026092001, new MigrateInboundEventsToMultipleOutcomes().Version);
    }

    [Fact]
    public async Task MigrationTransformsThePreviousInboundEventShape()
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
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);
        var sessionFactory = new NoTenancyByDefaultSessionFactory(
            store,
            new CurrentTenancyProviderNoTenancy());
        IRepository<InboundEvent> repository = new NoTenancyMartenRepository<InboundEvent>(sessionFactory);
        var inboundEvent = new ManualInboundEvent
        {
            Id = $"migration-test-{Guid.NewGuid():N}",
            ManualInputId = Guid.NewGuid().ToString("N"),
            Input = "Prepare two independent deliverables.",
            DedupeKey = Guid.NewGuid().ToString("N"),
            ReceivedAt = DateTimeOffset.UtcNow,
            HandledAt = DateTimeOffset.UtcNow,
            Status = InboundEventStatus.Processed,
            Explanation = "Created a work item.",
            Outcomes =
            [
                new InboundEventOutcome
                {
                    Id = Guid.NewGuid().ToString(),
                    Kind = InboundEventOutcomeKind.CreatedWorkItem,
                    WorkItemId = "work-item-1",
                    RelevantText = "Prepare two independent deliverables.",
                    Explanation = "Created a work item."
                }
            ]
        };
        await repository.AddAsync(inboundEvent);

        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    update samwise.mt_doc_inboundevent
                    set data = (data - 'Status' - 'Outcomes')
                        || jsonb_build_object(
                            'Handling', 'CreateWorkItem',
                            'WorkItemId', 'work-item-1')
                    where id = @id;
                    """;
                command.Parameters.AddWithValue("id", inboundEvent.Id);
                await command.ExecuteNonQueryAsync();
            }

            await using (var unitOfWork = new UnitOfWorkProvider())
            await using (var session = await sessionFactory.OpenSessionAsync(unitOfWork.Get()))
            {
                await new MigrateInboundEventsToMultipleOutcomes().MigrateAsync(
                    session,
                    CancellationToken.None);
                await session.SaveChangesAsync();
                await unitOfWork.CommitAsync();
            }

            var migrated = await repository.GetRequiredByIdAsync(inboundEvent.Id);
            Assert.Equal(InboundEventStatus.Processed, migrated.Status);
            var outcome = Assert.Single(migrated.Outcomes);
            Assert.Equal(InboundEventOutcomeKind.CreatedWorkItem, outcome.Kind);
            Assert.Equal("work-item-1", outcome.WorkItemId);
            Assert.Equal(inboundEvent.Input, outcome.RelevantText);
        }
        finally
        {
            await repository.DeleteByIdAsync(inboundEvent.Id);
        }
    }
}
