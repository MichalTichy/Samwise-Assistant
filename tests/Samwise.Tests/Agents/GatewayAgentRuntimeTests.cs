using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Samwise.Logic.Agents;
using Samwise.Infrastructure.AgentGateway.Agents;
using Samwise.Infrastructure.AgentRuntimes.Agents;

namespace Samwise.Tests.Agents;

public sealed class GatewayAgentRuntimeTests
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
        var profile = new GatewayProfileOptions
        {
            BaseAddress = "http://gateway.test/p/samwise-planning/v1/",
            ApiKey = "planning-test-key",
            Model = "planning-test-model",
            ReasoningEffort = "high"
        };
        var options = Options.Create(new CodexGatewayOptions
        {
            Intake = profile,
            Planning = profile,
            Execution = profile
        });
        var factory = new CatalogHttpClientFactory(profile.BaseAddress, catalog);
        using var gateway = new CodexGatewayClient(options, factory);

        var result = await gateway.GetToolsAsync(GatewayStage.Planning);

        Assert.Equal("sha256:catalog-test", result.Version);
        var tool = Assert.Single(result.Tools);
        Assert.Equal("documents/search", tool.Id);
        Assert.Equal("search", tool.Name);
        Assert.True(tool.CanInvoke);
        Assert.Equal("/p/samwise-planning/v1/tools", factory.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ClassifyAsyncUsesStrictJsonSchemaAndParsesStructuredResponse()
    {
        await using var server = new LoopbackServer();
        var profile = new GatewayProfileOptions
        {
            BaseAddress = server.BaseAddress.AbsoluteUri,
            ApiKey = "intake-test-key",
            Model = "intake-test-model",
            ReasoningEffort = "medium"
        };
        var options = Options.Create(new CodexGatewayOptions
        {
            Intake = profile,
            Planning = profile,
            Execution = profile,
            RequestTimeout = TimeSpan.FromSeconds(10)
        });
        using var gateway = new CodexGatewayClient(options, new UnusedHttpClientFactory());
        var runtime = new GatewayAgentRuntime(gateway);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseBody = JsonSerializer.Serialize(new
        {
            id = "chatcmpl-intake-test",
            @object = "chat.completion",
            created = 1_700_000_000,
            model = profile.Model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = JsonSerializer.Serialize(new
                        {
                            decision = "create_task",
                            existing_task_id = (string?)null,
                            proposed_title = "Prepare the revised offer",
                            proposed_description = "Prepare and deliver the revised customer offer.",
                            explanation = "The input asks for a concrete deliverable."
                        })
                    },
                    finish_reason = "stop"
                }
            },
            usage = new
            {
                prompt_tokens = 20,
                completion_tokens = 30,
                total_tokens = 50
            }
        });
        var capturedRequestTask = server.CaptureAndRespondAsync(responseBody, timeout.Token);

        var result = await runtime.ClassifyAsync(
            new IntakeRequest(
                "7cf225dd-347a-4ee9-9763-185fdc30daef",
                "Manual",
                "Classify this input: Prepare the revised offer."),
            timeout.Token);
        var request = await capturedRequestTask;

        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer intake-test-key", request.Headers["Authorization"]);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal(profile.Model, root.GetProperty("model").GetString());
        Assert.Equal("medium", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.TryGetProperty("stream", out _));
        Assert.Contains(
            root.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("content").GetString() is { } content &&
                content.Contains("Prepare the revised offer", StringComparison.Ordinal));

        var responseFormat = root.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        var jsonSchema = responseFormat.GetProperty("json_schema");
        Assert.True(
            jsonSchema.TryGetProperty("strict", out var strict) && strict.GetBoolean(),
            request.Body);
        var schemaProperties = jsonSchema
            .GetProperty("schema")
            .GetProperty("properties");
        Assert.True(schemaProperties.TryGetProperty("decision", out _));
        Assert.True(schemaProperties.TryGetProperty("existing_task_id", out _));
        Assert.True(schemaProperties.TryGetProperty("proposed_title", out _));
        Assert.True(schemaProperties.TryGetProperty("proposed_description", out _));
        Assert.True(schemaProperties.TryGetProperty("explanation", out _));
        var requiredProperties = jsonSchema
            .GetProperty("schema")
            .GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(schemaProperties.EnumerateObject().Count(), requiredProperties.Count);
        Assert.All(schemaProperties.EnumerateObject(), property => Assert.Contains(property.Name, requiredProperties));

        Assert.Equal(IntakeDecision.CreateTask, result.Decision);
        Assert.Null(result.ExistingTaskId);
        Assert.Equal("Prepare the revised offer", result.ProposedTitle);
        Assert.Equal("Prepare and deliver the revised customer offer.", result.ProposedDescription);
        Assert.Equal("The input asks for a concrete deliverable.", result.Explanation);
        Assert.Equal("Codex Gateway", result.Receipt.Provider);
        Assert.Equal(profile.Model, result.Receipt.Model);
        Assert.Equal("chatcmpl-intake-test", result.Receipt.CompletionId);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException($"The intake classification call should not request named client '{name}'.");
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

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public LoopbackServer()
        {
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            BaseAddress = new Uri($"http://127.0.0.1:{endpoint.Port}/v1/", UriKind.Absolute);
        }

        public Uri BaseAddress { get; }

        public async Task<CapturedRequest> CaptureAndRespondAsync(
            string responseBody,
            CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var headerBytes = await ReadHeadersAsync(stream, cancellationToken);
            var headerText = Encoding.ASCII.GetString(headerBytes);
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var headers = lines
                .Skip(1)
                .Where(line => line.Contains(':', StringComparison.Ordinal))
                .Select(line => line.Split(':', 2))
                .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var contentLength = int.Parse(headers["Content-Length"], System.Globalization.CultureInfo.InvariantCulture);
            var bodyBytes = new byte[contentLength];
            await stream.ReadExactlyAsync(bodyBytes, cancellationToken);

            var responseBytes = Encoding.UTF8.GetBytes(responseBody);
            var responseHeaders = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {responseBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(responseHeaders, cancellationToken);
            await stream.WriteAsync(responseBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            return new CapturedRequest(
                requestLine[0],
                requestLine[1],
                headers,
                Encoding.UTF8.GetString(bodyBytes));
        }

        public ValueTask DisposeAsync()
        {
            listener.Stop();
            return ValueTask.CompletedTask;
        }

        private static async Task<byte[]> ReadHeadersAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var terminatorState = 0;
            while (terminatorState < 4)
            {
                var value = await ReadByteAsync(stream, cancellationToken);
                buffer.WriteByte(value);
                terminatorState = (terminatorState, value) switch
                {
                    (0, (byte)'\r') => 1,
                    (1, (byte)'\n') => 2,
                    (2, (byte)'\r') => 3,
                    (3, (byte)'\n') => 4,
                    (_, (byte)'\r') => 1,
                    _ => 0
                };
            }

            return buffer.ToArray();
        }

        private static async Task<byte> ReadByteAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var singleByte = new byte[1];
            var count = await stream.ReadAsync(singleByte, cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException("The client disconnected before completing its HTTP request.");
            }

            return singleByte[0];
        }
    }

    private sealed record CapturedRequest(
        string Method,
        string Path,
        IReadOnlyDictionary<string, string> Headers,
        string Body);
}
