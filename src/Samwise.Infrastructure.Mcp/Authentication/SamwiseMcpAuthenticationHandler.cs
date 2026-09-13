using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.Mcp.Configuration;

namespace Samwise.Infrastructure.Mcp.Authentication;

public sealed class SamwiseMcpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<SamwiseMcpOptions> mcpOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedApiKey = mcpOptions.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("The Samwise MCP API key is not configured."));
        }

        if (!Request.Headers.TryGetValue(SamwiseMcpAuthenticationDefaults.HeaderName, out var values) ||
            values.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedApiKey = values[0];
        if (string.IsNullOrEmpty(providedApiKey) || !KeysMatch(expectedApiKey, providedApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("The Samwise MCP API key is invalid."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "samwise-mcp")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool KeysMatch(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
