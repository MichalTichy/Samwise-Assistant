using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.Mcp.Authentication;
using Samwise.Infrastructure.Mcp.Configuration;

namespace Samwise.Tests.Mcp;

public sealed class McpAuthenticationHandlerTests
{
    [Fact]
    public async Task CorrectApiKeyAuthenticatesTheMcpCaller()
    {
        var handler = CreateHandler("expected-key");
        var context = new DefaultHttpContext();
        context.Request.Headers[SamwiseMcpAuthenticationDefaults.HeaderName] = "expected-key";
        await InitializeAsync(handler, context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("samwise-mcp", result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public async Task IncorrectApiKeyDoesNotAuthenticateTheMcpCaller()
    {
        var handler = CreateHandler("expected-key");
        var context = new DefaultHttpContext();
        context.Request.Headers[SamwiseMcpAuthenticationDefaults.HeaderName] = "different-key";
        await InitializeAsync(handler, context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    private static SamwiseMcpAuthenticationHandler CreateHandler(string apiKey) =>
        new(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new StaticOptionsMonitor<SamwiseMcpOptions>(new SamwiseMcpOptions { ApiKey = apiKey }));

    private static Task InitializeAsync(
        SamwiseMcpAuthenticationHandler handler,
        HttpContext context) =>
        handler.InitializeAsync(
            new AuthenticationScheme(
                SamwiseMcpAuthenticationDefaults.Scheme,
                SamwiseMcpAuthenticationDefaults.Scheme,
                typeof(SamwiseMcpAuthenticationHandler)),
            context);

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
