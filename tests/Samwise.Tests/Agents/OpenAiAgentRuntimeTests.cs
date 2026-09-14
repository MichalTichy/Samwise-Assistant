using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Logic.Agents;

namespace Samwise.Tests.Agents;

public sealed class OpenAiAgentRuntimeTests
{
    [Fact]
    public async Task ClassifyAsyncUsesStrictJsonSchemaAndParsesStructuredResponse()
    {
        await using var server = new LoopbackServer();
        const string model = "intake-test-model";
        var runtime = CreateRuntime(server.BaseAddress, "intake-test-key");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseBody = CreateChatCompletion(model, new
        {
            decision = "create_work_item",
            existing_work_item_id = (string?)null,
            proposed_title = "Prepare the revised offer",
            proposed_description = "Prepare and deliver the revised customer offer.",
            explanation = "The input asks for a concrete deliverable."
        });
        var capturedRequestTask = server.CaptureAndRespondAsync(responseBody, timeout.Token);

        var result = await runtime.ClassifyAsync(
            new IntakeRequest(
                "7cf225dd-347a-4ee9-9763-185fdc30daef",
                "Manual",
                "Classify this input: Prepare the revised offer.",
                new AgentRunConfiguration(model, "max")),
            timeout.Token);
        var request = await capturedRequestTask;

        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer intake-test-key", request.Headers["Authorization"]);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal(model, root.GetProperty("model").GetString());
        Assert.Equal("max", root.GetProperty("reasoning_effort").GetString());
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
        Assert.True(schemaProperties.TryGetProperty("existing_work_item_id", out _));
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

        Assert.Equal(IntakeDecision.CreateWorkItem, result.Decision);
        Assert.Null(result.ExistingWorkItemId);
        Assert.Equal("Prepare the revised offer", result.ProposedTitle);
        Assert.Equal("Prepare and deliver the revised customer offer.", result.ProposedDescription);
        Assert.Equal("The input asks for a concrete deliverable.", result.Explanation);
        Assert.Equal("OpenAI Chat Completions", result.Receipt.Provider);
        Assert.Equal(model, result.Receipt.Model);
        Assert.Equal("chatcmpl-test", result.Receipt.CompletionId);
    }

    [Fact]
    public async Task PlanningUsesConfiguredModelAndEffortAndReturnsSelectedPlaybook()
    {
        await using var server = new LoopbackServer();
        const string model = "planning-model";
        var runtime = CreateRuntime(server.BaseAddress, "planning-test-key");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseBody = CreateChatCompletion(model, new
        {
            disposition = "plan_required",
            resolution_reason = (string?)null,
            playbook_id = "invoice-processing",
            summary = "Process the invoice safely.",
            assumptions = Array.Empty<string>(),
            risks = Array.Empty<string>(),
            steps = new[]
            {
                new
                {
                    order = 1,
                    title = "Create draft",
                    technical_description = "Create a draft accounting entry.",
                    tool_calls = new[]
                    {
                        new
                        {
                            tool_id = "accounting/create-draft",
                            arguments_json = "{\"invoice_id\":\"invoice-1\"}"
                        }
                    },
                    has_external_side_effects = true,
                    success_criteria = "The draft exists."
                }
            }
        });
        var capturedRequestTask = server.CaptureAndRespondAsync(responseBody, timeout.Token);
        var catalog = new ToolCatalogSnapshot(
            "catalog",
            "{}",
            [new ToolServerCapability(
                "accounting",
                "Accounting",
                "1.0.0",
                false,
                null,
                null,
                null,
                [new ToolCapability("accounting/create-draft", "create-draft", null, "{}", true)])]);

        var result = await runtime.PlanAsync(
            new PlannerRequest(
                "work-item-1",
                1,
                1,
                "Plan this invoice.",
                catalog,
                catalog,
                new AgentRunConfiguration(model, "max")),
            timeout.Token);
        var request = await capturedRequestTask;

        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal(model, document.RootElement.GetProperty("model").GetString());
        Assert.Equal("max", document.RootElement.GetProperty("reasoning_effort").GetString());
        var planningSchemaProperties = document.RootElement
            .GetProperty("response_format")
            .GetProperty("json_schema")
            .GetProperty("schema")
            .GetProperty("properties");
        Assert.True(planningSchemaProperties.TryGetProperty("disposition", out _));
        Assert.True(planningSchemaProperties.TryGetProperty("resolution_reason", out _));
        Assert.Equal(PlanningDisposition.PlanRequired, result.Disposition);
        Assert.Null(result.ResolutionReason);
        Assert.Equal("invoice-processing", result.Plan.PlaybookId);
        Assert.Equal("Process the invoice safely.", result.Plan.Summary);
        var step = Assert.Single(result.Plan.Steps);
        var toolCall = Assert.Single(step.ToolCalls);
        Assert.Equal("accounting/create-draft", toolCall.ToolId);
        Assert.Equal("{\"invoice_id\":\"invoice-1\"}", toolCall.ArgumentsJson);
    }

    [Theory]
    [InlineData("already_resolved", PlanningDisposition.AlreadyResolved)]
    [InlineData("no_longer_relevant", PlanningDisposition.NoLongerRelevant)]
    public async Task PlanningParsesTerminalDisposition(
        string wireDisposition,
        PlanningDisposition expectedDisposition)
    {
        await using var server = new LoopbackServer();
        const string model = "planning-model";
        var runtime = CreateRuntime(server.BaseAddress, "planning-test-key");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseBody = CreateChatCompletion(model, new
        {
            disposition = wireDisposition,
            resolution_reason = "The latest sent email resolved the objective.",
            playbook_id = (string?)null,
            summary = string.Empty,
            assumptions = Array.Empty<string>(),
            risks = Array.Empty<string>(),
            steps = Array.Empty<object>()
        });
        var capturedRequestTask = server.CaptureAndRespondAsync(responseBody, timeout.Token);
        var emptyCatalog = new ToolCatalogSnapshot("catalog", "{}", []);

        var result = await runtime.PlanAsync(
            new PlannerRequest(
                "work-item-1",
                2,
                2,
                "Reassess this work item.",
                emptyCatalog,
                emptyCatalog,
                new AgentRunConfiguration(model, "medium")),
            timeout.Token);
        await capturedRequestTask;

        Assert.Equal(expectedDisposition, result.Disposition);
        Assert.Equal("The latest sent email resolved the objective.", result.ResolutionReason);
        Assert.Empty(result.Plan.Steps);
    }

    private static OpenAiAgentRuntime CreateRuntime(Uri baseAddress, string apiKey)
    {
        var profile = new AgentEndpointProfileOptions
        {
            BaseAddress = baseAddress.AbsoluteUri,
            ApiKey = apiKey
        };
        return new OpenAiAgentRuntime(Options.Create(new AgentEndpointOptions
        {
            Intake = profile,
            Planning = profile,
            Execution = profile,
            RequestTimeout = TimeSpan.FromSeconds(10)
        }));
    }

    private static string CreateChatCompletion(string model, object content) => JsonSerializer.Serialize(new
    {
        id = "chatcmpl-test",
        @object = "chat.completion",
        created = 1_700_000_000,
        model,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new
                {
                    role = "assistant",
                    content = JsonSerializer.Serialize(content)
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
