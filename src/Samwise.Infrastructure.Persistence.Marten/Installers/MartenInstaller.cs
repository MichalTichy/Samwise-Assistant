using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Shared.Infrastructure.IoC;
using Shared.Infrastructure.Persistence.Marten.Extensions;
using Shared.Infrastructure.Persistence.Marten.StorageConfiguration;

namespace Samwise.Infrastructure.Persistence.Marten.Installers;

public sealed class MartenInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        var connectionString = configuration.GetConnectionString("samwise")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'samwise' was not configured.");

        services.AddSingleton<IStorageConfiguration, SamwiseStorageConfiguration>();
        services.AddNpgsqlDataSource(connectionString);
        // Keep offsets while reading projections through JToken; the shared default parses them as
        // unspecified DateTime values, which shifts a UTC task time into the server's local zone.
        services.AddMartenPostgresPersistence(useTenancy: false,
            configureSerialization: settings => settings.DateParseHandling = Newtonsoft.Json.DateParseHandling.DateTimeOffset);
    }
}
