using JasperFx;
using Marten;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Marten.Migration;
using Weasel.Core;

namespace Samwise.Infrastructure.Persistence.Marten.Initialization;

public sealed class SamwiseDatabaseMigrationInitializer(
    IDocumentStore store,
    IDatabaseMigrator migrator) : InitializerBase
{
    public override int Priority => int.MaxValue;
    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;
    public override bool RunOnlyInLeaderInstance => true;

    protected override async Task RunInitializationLogicAsync()
    {
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate);
        await migrator.EnsureMigrationHistoryTableAsync();
        await migrator.RunMigrationsAsync();
    }
}
