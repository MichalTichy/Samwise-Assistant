using JasperFx;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Persistence.Marten.Agents;
using Samwise.Infrastructure.Persistence.Marten.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Prompts;
using Samwise.Infrastructure.Persistence.Marten.Storage;
using Samwise.Infrastructure.Persistence.Marten.Tasks;
using Samwise.Infrastructure.Persistence.Marten.Time;
using Samwise.Logic.Inbox;
using Samwise.Logic.Prompts;
using Samwise.Logic.Tasks;
using Samwise.Logic.Time;
using Shared.Infrastructure.IoC;
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
        services.AddMarten(serviceProvider =>
            {
                var options = new StoreOptions();
                options.Connection(connectionString);
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

                foreach (var storageConfiguration in serviceProvider.GetServices<IStorageConfiguration>())
                {
                    storageConfiguration.Configure(options);
                }

                return options;
            })
            .UseLightweightSessions();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITaskStore, MartenTaskStore>();
        services.AddScoped<IInboundEventStore, MartenInboundEventStore>();
        services.AddScoped<IPromptTemplateStore, MartenPromptTemplateStore>();
        services.AddScoped<AgentWorkProcessor>();
        services.AddHostedService<AgentWorker>();
    }
}
