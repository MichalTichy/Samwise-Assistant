using Samwise.Logic.Agents;
using Samwise.Domain.Inbox;
using Samwise.Infrastructure.Persistence.Marten.Agents;

namespace Samwise.Tests.Agents;

public sealed class AgentWorkProcessorTests
{
    private static readonly AgentReceipt Receipt = new(
        "Test",
        "test-model",
        "test-completion",
        "{}");

    [Fact]
    public void NewTaskPreservesInboundTextInsteadOfAiDescription()
    {
        const string inboundText = "Prepare the revised offer.\nInclude the signed appendix and delivery dates.";
        var inboundEvent = new InboundEvent
        {
            Id = Guid.NewGuid(),
            Source = "Manual",
            ExternalId = "manual:test",
            Text = inboundText
        };
        var result = new IntakeResult(
            IntakeDecision.CreateTask,
            null,
            "Revised customer offer",
            "AI-generated summary that must not replace the source text.",
            "This is actionable.",
            Receipt);

        var task = AgentWorkProcessor.CreateTaskFromIntake(
            inboundEvent,
            result,
            new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal("Revised customer offer", task.Title);
        Assert.Equal(inboundText, task.Description);
        Assert.NotEqual(result.ProposedDescription, task.Description);
    }

    [Fact]
    public void ExecutionCommentIncludesEveryOutputWithoutFlatteningLines()
    {
        var result = new ExecutionResult(
            true,
            "Execution completed.",
            [
                "Created offer.pdf\nSaved to the customer folder.",
                "Sent confirmation to sales."
            ],
            Receipt);

        var body = AgentWorkProcessor.FormatExecutionResult(result);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Execution completed.",
                string.Empty,
                "Outputs:",
                "Created offer.pdf\nSaved to the customer folder.",
                "Sent confirmation to sales."),
            body);
    }
}
