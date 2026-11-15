using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Inputs.Email;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;
using Samwise.Web.Endpoints.Setup;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Setup;

public sealed class SetupApiTests
{
    [Theory]
    [InlineData(null, "anything", HttpStatusCode.NotFound)]
    [InlineData("setup-secret", null, HttpStatusCode.Unauthorized)]
    [InlineData("setup-secret", "mcp-secret", HttpStatusCode.Unauthorized)]
    [InlineData("setup-secret", "wrong", HttpStatusCode.Unauthorized)]
    [InlineData("setup-secret", "setup-secret", HttpStatusCode.OK)]
    public async Task SetupRequiresItsOwnExplicitCredential(string? configured, string? supplied, HttpStatusCode expected)
    {
        await using var app = CreateApp(configured);
        await app.StartAsync();
        using var client = CreateClient(app, supplied);
        // A cookie is not an alternative credential for setup administration.
        client.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Identity.Application=owner-cookie");
        using var response = await client.GetAsync("/api/setup/status");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SetupReadsNeverExposeEndpointOrInputCredentials()
    {
        await using var app = CreateApp("setup-secret");
        await app.StartAsync();
        using var client = CreateClient(app, "setup-secret");
        foreach (var path in new[] { "/api/setup/status", "/api/setup/inputs" })
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-endpoint-secret", json);
            Assert.DoesNotContain("protected-token-secret", json);
            Assert.DoesNotContain("protected-mail-secret", json);
            Assert.DoesNotContain("protected-client-secret", json);
            Assert.DoesNotContain("setup-secret", json);
            Assert.DoesNotContain("https://user:private-endpoint-secret", json);
        }
    }

    [Fact]
    public async Task UnknownStageCannotReachModelDiscoveryOrSave()
    {
        await using var app = CreateApp("setup-secret");
        await app.StartAsync();
        using var client = CreateClient(app, "setup-secret");
        using var models = await client.GetAsync("/api/setup/agents/999/models");
        Assert.Equal(HttpStatusCode.BadRequest, models.StatusCode);
        using var save = await client.PutAsJsonAsync("/api/setup/agents/999", new { model = "test", reasoningEffort = "medium" });
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Null(app.Services.GetRequiredService<CapturingSender>().SavedStage);
    }

    [Fact]
    public async Task SelectingModelPreservesTheCurrentPromptWhenItIsOmitted()
    {
        await using var app = CreateApp("setup-secret");
        await app.StartAsync();
        using var client = CreateClient(app, "setup-secret");
        using var response = await client.PutAsJsonAsync("/api/setup/agents/Planning", new { model = "test-model", reasoningEffort = "high" });
        response.EnsureSuccessStatusCode();
        var sent = app.Services.GetRequiredService<CapturingSender>().SavedStage;
        Assert.NotNull(sent);
        Assert.Equal("Existing trusted instructions {{work_item}}", sent.Prompt);
        Assert.Equal("test-model", sent.Model);
        Assert.Equal("high", sent.ReasoningEffort);
    }

    private static WebApplication CreateApp(string? key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Setup:ApiKey"] = key,
            ["SamwiseMcp:ApiKey"] = "mcp-secret"
        });
        builder.Services.Configure<AgentEndpointOptions>(options =>
        {
            options.Planning.ApiKey = "private-endpoint-secret";
            options.Planning.BaseAddress = "https://user:private-endpoint-secret@example.com/v1/";
        });
        builder.Services.AddSingleton(typeof(IReadOnlyRepository<>), typeof(EmptyRepository<>));
        builder.Services.AddSingleton<IReadOnlyRepository<AgentConfiguration>>(new EmptyRepository<AgentConfiguration>(new AgentConfiguration
        {
            Planning = new AgentStageConfiguration { Prompt = "Existing trusted instructions {{work_item}}" }
        }));
        builder.Services.AddSingleton<IReadOnlyRepository<TodoistInputConfiguration>>(new EmptyRepository<TodoistInputConfiguration>(new TodoistInputConfiguration
        {
            ProtectedApiToken = "protected-token-secret"
        }));
        builder.Services.AddSingleton<IReadOnlyRepository<TeamsInputConfiguration>>(new EmptyRepository<TeamsInputConfiguration>(new TeamsInputConfiguration
        {
            MicrosoftGraph = new TeamsGraphApplicationConfiguration { ProtectedClientSecret = "protected-client-secret" }
        }));
        builder.Services.AddSingleton<IReadOnlyRepository<EmailAccount>>(new EmptyRepository<EmailAccount>(new EmailAccount
        {
            ProtectedPassword = "protected-mail-secret"
        }));
        builder.Services.AddSingleton<CapturingSender>();
        builder.Services.AddSingleton<ISender>(provider => provider.GetRequiredService<CapturingSender>());
        builder.Services.AddSingleton<IAgentModelCatalog, EmptyModelCatalog>();
        builder.Services.AddSingleton<IAgentPromptPlaceholderCatalog, EmptyPlaceholderCatalog>();
        builder.Services.AddSingleton<ITeamsAuthorizationService, EmptyTeamsAuthorization>();
        var app = builder.Build();
        // Match the host's HTML error middleware: without the setup filter's opt-out,
        // disabled/unauthorized API responses would be replaced by this redirect.
        app.UseStatusCodePagesWithReExecute("/not-found");
        app.MapGet("/not-found", () => Results.Redirect("/login"));
        app.MapSamwiseSetupApi();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app, string? key)
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        if (key is not null) client.DefaultRequestHeaders.Add("X-Setup-Key", key);
        return client;
    }

    private sealed class EmptyModelCatalog : IAgentModelCatalog
    {
        public Task<IReadOnlyList<AgentModelDefinition>> GetModelsAsync(AgentStage stage, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentModelDefinition>>([]);
    }

    private sealed class EmptyPlaceholderCatalog : IAgentPromptPlaceholderCatalog
    {
        public IReadOnlyList<PromptPlaceholder> GetForStage(AgentStage stage) => [];
    }

    private sealed class EmptyTeamsAuthorization : ITeamsAuthorizationService
    {
        public Task<TeamsConnectionState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TeamsConnectionState(false, null, null, null));
        public Task<TeamsAuthorizationChallenge> StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingSender : ISender
    {
        public SaveAgentStageConfigurationUsecase? SavedStage { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SavedStage = Assert.IsType<SaveAgentStageConfigurationUsecase>(request);
            return Task.FromResult((TResponse)(object)AgentStageConfigurationSaveResult.Success);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyRepository<T> : IReadOnlyRepository<T> where T : class, IItemWithId
    {
        private readonly T? item;
        public EmptyRepository() { }
        public EmptyRepository(T item) => this.item = item;
        public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) => Task.FromResult(item);
        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) => Task.FromResult<IReadOnlyList<T>>(item is null ? [] : [item]);
        public Task<IReadOnlyList<T>> ListAsync(IListSpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) => ListAsync(cancellationToken, tenantId);
        public Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<T?> GetBySpecAsync(ISpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) => throw new NotSupportedException();
    }
}
