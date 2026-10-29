using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Samwise.Domain.Agents;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Infrastructure.CodexGateway.Catalogs;
using Samwise.Infrastructure.Mcp.Tools;
using Samwise.Logic.Agents;

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

        var result = await client.GetToolsAsync(AgentStage.Planning);

        Assert.Equal("sha256:catalog-test", result.CatalogVersion);
        var server = Assert.Single(result.Servers);
        Assert.Equal("documents", server.Id);
        Assert.Equal("Documents", server.Name);
        Assert.Equal("1.0.0", server.Version);
        Assert.Equal("Document tools", server.Description);
        var tool = Assert.Single(result.Tools);
        Assert.Equal("documents/search", tool.Id);
        Assert.Equal("search", tool.Name);
        Assert.True(tool.CanInvoke);
        Assert.Equal("/p/samwise-planning/v1/tools", factory.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task NativeShellCapabilityCanBeResolvedAndUsedInAReviewedDocumentPlan()
    {
        const string catalog = """
            {
              "object": "list",
              "catalog_version": "sha256:workspace-shell",
              "data": [{
                "id": "codex-built-in",
                "name": "Codex built-in tools",
                "version": "2",
                "required": false,
                "tools": [{
                  "id": "codex-built-in/shell",
                  "name": "shell",
                  "description": "Process local documents using the native shell and openpyxl.",
                  "input_schema": {
                    "type": "object",
                    "properties": { "command": { "type": "string", "minLength": 1 } },
                    "required": ["command"],
                    "additionalProperties": false
                  },
                  "_meta": { "source": "codex", "execution": "native-shell" }
                }]
              }]
            }
            """;
        var (client, _) = CreateClient(catalog);
        var executionCatalog = await client.GetToolsAsync(AgentStage.Execution);
        var schemaJson = await new ExecutionToolCatalogTools(client)
            .GetExecutionToolSchemaAsync("codex-built-in/shell");
        using var schema = JsonDocument.Parse(schemaJson);
        Assert.True(schema.RootElement.GetProperty("found").GetBoolean());
        var tool = schema.RootElement.GetProperty("tool");
        Assert.Equal("string", tool.GetProperty("input_schema").GetProperty("properties")
            .GetProperty("command").GetProperty("type").GetString());

        var plan = new PlanDraft(null, "Prepare a workbook for review.", [], [],
            [new PlanStepDraft(1, "Fill the workbook", "Use openpyxl on the supplied template.",
                [new PlanToolCallDraft("codex-built-in/shell", """{"command":"python3 ./artifacts/fill_workbook.py"}""")],
                false, "The completed workbook is available in ./artifacts for the approved handoff.")]);

        AgentWorkflowSupport.ValidatePlan(plan, executionCatalog);
    }

    [Theory]
    [InlineData(AgentStage.Intake, "/v1/tools")]
    [InlineData(AgentStage.Planning, "/p/samwise-planning/v1/tools")]
    [InlineData(AgentStage.Execution, "/p/samwise-execution/v1/tools")]
    public async Task ToolCatalogUsesTheSelectedStageProfile(AgentStage stage, string expectedPath)
    {
        const string catalog =
            "{\"catalog_version\":\"sha256:empty\",\"data\":[],\"object\":\"list\"}";
        var (client, factory) = CreateClient(catalog);

        await client.GetToolsAsync(stage);

        Assert.Equal(expectedPath, factory.RequestUri?.AbsolutePath);
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
        var intakeProfile = new AgentEndpointProfileOptions
        {
            BaseAddress = "http://gateway.test/v1/",
            ApiKey = "intake-test-key"
        };
        var planningProfile = new AgentEndpointProfileOptions
        {
            BaseAddress = "http://gateway.test/p/samwise-planning/v1/",
            ApiKey = "planning-test-key"
        };
        var executionProfile = new AgentEndpointProfileOptions
        {
            BaseAddress = "http://gateway.test/p/samwise-execution/v1/",
            ApiKey = "execution-test-key"
        };
        var options = Options.Create(new AgentEndpointOptions
        {
            Intake = intakeProfile,
            Planning = planningProfile,
            Execution = executionProfile
        });
        var factory = new CatalogHttpClientFactory(
            intakeProfile.BaseAddress,
            planningProfile.BaseAddress,
            executionProfile.BaseAddress,
            responseBody);
        return (new CodexGatewayCatalogClient(options, factory), factory);
    }

    private sealed class CatalogHttpClientFactory(
        string intakeBaseAddress,
        string planningBaseAddress,
        string executionBaseAddress,
        string responseBody) : IHttpClientFactory
    {
        public Uri? RequestUri { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this, responseBody))
        {
            BaseAddress = new Uri(name switch
            {
                CodexGatewayCatalogClient.IntakeHttpClientName => intakeBaseAddress,
                CodexGatewayCatalogClient.PlanningHttpClientName => planningBaseAddress,
                CodexGatewayCatalogClient.ExecutionHttpClientName => executionBaseAddress,
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
            }, UriKind.Absolute)
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
