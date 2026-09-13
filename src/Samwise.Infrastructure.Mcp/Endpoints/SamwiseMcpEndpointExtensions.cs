using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Samwise.Infrastructure.Mcp.Authentication;

namespace Samwise.Infrastructure.Mcp.Endpoints;

public static class SamwiseMcpEndpointExtensions
{
    public static IEndpointConventionBuilder MapSamwiseMcp(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapMcp("/mcp")
            .RequireAuthorization(SamwiseMcpAuthenticationDefaults.Policy);
}
