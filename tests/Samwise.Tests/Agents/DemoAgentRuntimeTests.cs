using Samwise.Logic.Agents;
using Samwise.Infrastructure.AgentGateway.Agents;

namespace Samwise.Tests.Agents;

public sealed class DemoAgentRuntimeTests
{
    [Fact]
    public async Task ManualInputIsClassifiedAsANewActionableTask()
    {
        var runtime = new DemoAgentRuntime();
        var request = new IntakeRequest(
            Guid.NewGuid(),
            "Manual",
            "Classify this input: Prepare the revised offer.");

        var result = await runtime.ClassifyAsync(request, CancellationToken.None);

        Assert.Equal(IntakeDecision.CreateTask, result.Decision);
        Assert.Null(result.ExistingTaskId);
        Assert.Contains("actionable task", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Development demo", result.Receipt.Provider);
        Assert.False(string.IsNullOrWhiteSpace(result.Receipt.RawResponse));
    }
}
