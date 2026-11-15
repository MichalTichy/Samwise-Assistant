using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;

namespace Samwise.Web.Endpoints.Setup;

/// <summary>The setup credential is separate from both owner cookies and agent MCP access.</summary>
public sealed class SetupAccessFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // API status codes must not be re-executed through an owner-only HTML page.
        if (context.HttpContext.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
        {
            statusPages.Enabled = false;
        }

        var expected = configuration["Setup:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
        {
            return Results.NotFound();
        }

        var supplied = context.HttpContext.Request.Headers["X-Setup-Key"];
        if (supplied.Count != 1 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0] ?? string.Empty))))
        {
            return Results.Unauthorized();
        }

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }
}
