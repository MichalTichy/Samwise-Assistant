using Samwise.Domain.Agents;
using Samwise.Logic.Prompts;

namespace Samwise.Tests.Prompts;

public sealed class PromptRendererTests
{
    private readonly PromptRenderer _renderer = new();

    [Fact]
    public void RenderReplacesKnownPlaceholdersIncludingWhitespaceAndEmptyValues()
    {
        const string template = "Input: {{ input.text }}\nWork items: {{active_work_items}}\nAt: {{current_time}}";
        var values = new Dictionary<string, string?>
        {
            ["input.text"] = "Please prepare the offer.",
            ["active_work_items"] = null,
            ["current_time"] = "2026-08-17T08:00:00Z"
        };

        var result = _renderer.Render(AgentStage.Intake, template, values);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(
            "Input: Please prepare the offer.\nWork items: \nAt: 2026-08-17T08:00:00Z",
            result.Text);
    }

    [Fact]
    public void ValidateRejectsPlaceholderThatDoesNotBelongToTheStage()
    {
        var errors = _renderer.Validate(AgentStage.Intake, "Work item: {{work_item.title}}");

        var error = Assert.Single(errors);
        Assert.Equal("Unknown placeholder '{{work_item.title}}'.", error);
    }

    [Theory]
    [InlineData("Input: {{input.text}")]
    [InlineData("Input: {input.text}}")]
    [InlineData("Input: {{Input.Text}}")]
    public void ValidateRejectsMalformedPlaceholder(string template)
    {
        var errors = _renderer.Validate(AgentStage.Intake, template);

        Assert.Contains("A placeholder is malformed. Use the exact form {{placeholder}}.", errors);
    }

    [Fact]
    public void RenderFailsWhenAReferencedPlaceholderHasNoValue()
    {
        var result = _renderer.Render(
            AgentStage.Intake,
            "Input: {{input.text}}",
            new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Empty(result.Text);
        Assert.Equal("No value was supplied for '{{input.text}}'.", Assert.Single(result.Errors));
    }

    [Fact]
    public void PlanningPromptAcceptsThePlaybooksPlaceholder()
    {
        var errors = _renderer.Validate(AgentStage.Planning, "Playbooks: {{playbooks}}");

        Assert.Empty(errors);
    }

    [Fact]
    public void IntakePromptAcceptsBothDownstreamToolCatalogPlaceholders()
    {
        var errors = _renderer.Validate(
            AgentStage.Intake,
            "Planning: {{planning_tools}} Execution: {{execution_tools}}");

        Assert.Empty(errors);
    }
}
