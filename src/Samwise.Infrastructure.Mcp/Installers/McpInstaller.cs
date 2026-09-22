using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using Samwise.Infrastructure.Mcp.Authentication;
using Samwise.Infrastructure.Mcp.Configuration;
using Samwise.Infrastructure.Mcp.Tools;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Mcp.Installers;

public sealed class McpInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        services.Configure<SamwiseMcpOptions>(
            configuration.GetSection(SamwiseMcpOptions.SectionName));
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SamwiseMcpAuthenticationHandler>(
                SamwiseMcpAuthenticationDefaults.Scheme,
                _ => { });
        services.AddAuthorization(options => options.AddPolicy(
            SamwiseMcpAuthenticationDefaults.Policy,
            policy => policy
                .AddAuthenticationSchemes(SamwiseMcpAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser()));

        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<WorkItemTools>()
            .WithTools<WorkItemActionTools>()
            .WithTools<AgentInvocationTools>()
            .WithTools<InputEventTools>()
            .WithTools<PlaybookTools>()
            .WithTools<ExecutionToolCatalogTools>()
            .WithTools<AgentSettingsTools>();
    }
}
