using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Infrastructure.CodexGateway.Catalogs;

namespace Samwise.Tests.Agents;

public sealed class CodexGatewayCatalogTests
{
    [Fact]
    public async Task ToolCatalogUsesFinalGatewayShapeWhereEveryReturnedToolIsEnabled()
    {
        const string catalog = """
            {
              "object": "list",
              "catalog_version": "sha256:catalog-test",
              "data": [
                {
                  "id": "documents",
                  "name": "Documents",
                  "version": "1.0.0",
                  "required": false,
                  "title": "Documents",
                  "description": "Document tools",
                  "website_url": null,
                  "icons": null,
                  "tools": [
                    {
                      "id": "documents/search",
                      "name": "search",
                      "title": "Search",
                      "description": "Search documents",
                      "input_schema": { "type": "object" },
                      "output_schema": null,
                      "annotations": null,
                      "icons": null,
                      "_meta": null
                    }
                  ]
                }
              ]
            }
            """;
        var (client, factory) = CreateClient(catalog);

        var result = await client.GetPlanningToolsAsync();

        Assert.Equal("sha256:catalog-test", result.CatalogVersion);
        var tool = Assert.Single(result.Tools);
        Assert.Equal("documents/search", tool.Id);
        Assert.Equal("search", tool.Name);
        Assert.True(tool.CanInvoke);
        Assert.Equal("/p/samwise-planning/v1/tools", factory.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ModelCatalogPreservesGatewaySpecificReasoningEfforts()
    {
        const string catalog = """
            {
              "object": "list",
              "data": [
                {
                  "id": "gpt-5.6-sol",
                  "object": "model",
                  "created": 0,
                  "owned_by": "openai",
                  "supported_reasoning_efforts": ["none", "medium", "xhigh", "max"],
                  "default_reasoning_effort": "medium"
                }
              ]
            }
            """;
        var (client, factory) = CreateClient(catalog);

        var result = await client.GetModelsAsync(AgentStage.Planning);

        var model = Assert.Single(result);
        Assert.Equal("gpt-5.6-sol", model.Id);
        Assert.Equal(["none", "medium", "xhigh", "max"], model.SupportedReasoningEfforts);
        Assert.Equal("medium", model.DefaultReasoningEffort);
        Assert.Equal("/p/samwise-planning/v1/models", factory.RequestUri?.AbsolutePath);
    }

    private static (CodexGatewayCatalogClient Client, CatalogHttpClientFactory Factory) CreateClient(
        string responseBody)
    {
        var profile = new AgentEndpointProfileOptions
        {
            BaseAddress = "http://gateway.test/p/samwise-planning/v1/",
            ApiKey = "planning-test-key"
        };
        var options = Options.Create(new AgentEndpointOptions
        {
            Intake = profile,
            Planning = profile,
            Execution = profile
        });
        var factory = new CatalogHttpClientFactory(profile.BaseAddress, responseBody);
        return (new CodexGatewayCatalogClient(options, factory), factory);
    }

    private sealed class CatalogHttpClientFactory(string baseAddress, string responseBody) : IHttpClientFactory
    {
        public Uri? RequestUri { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this, responseBody))
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute)
        };

        private sealed class Handler(CatalogHttpClientFactory owner, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.RequestUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
