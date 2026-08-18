using Samwise.Domain.Prompts;
using Samwise.Logic.Prompts;

namespace Samwise.Tests.Prompts;

public sealed class PromptRendererTests
{
    private readonly PromptRenderer _renderer = new();

    [Fact]
    public void RenderReplacesKnownPlaceholdersIncludingWhitespaceAndEmptyValues()
    {
        const string template = "Input: {{ input.text }}\nTasks: {{active_tasks}}\nAt: {{current_time}}";
        var values = new Dictionary<string, string?>
        {
            ["input.text"] = "Please prepare the offer.",
            ["active_tasks"] = null,
            ["current_time"] = "2026-08-17T08:00:00Z"
        };

        var result = _renderer.Render(PromptStage.Intake, template, values);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(
            "Input: Please prepare the offer.\nTasks: \nAt: 2026-08-17T08:00:00Z",
            result.Text);
    }

    [Fact]
    public void ValidateRejectsPlaceholderThatDoesNotBelongToTheStage()
    {
        var errors = _renderer.Validate(PromptStage.Intake, "Task: {{task.title}}");

        var error = Assert.Single(errors);
        Assert.Equal("Unknown placeholder '{{task.title}}'.", error);
    }

    [Theory]
    [InlineData("Input: {{input.text}")]
    [InlineData("Input: {input.text}}")]
    [InlineData("Input: {{Input.Text}}")]
    public void ValidateRejectsMalformedPlaceholder(string template)
    {
        var errors = _renderer.Validate(PromptStage.Intake, template);

        Assert.Contains("A placeholder is malformed. Use the exact form {{placeholder}}.", errors);
    }

    [Fact]
    public void RenderFailsWhenAReferencedPlaceholderHasNoValue()
    {
        var result = _renderer.Render(
            PromptStage.Intake,
            "Input: {{input.text}}",
            new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Empty(result.Text);
        Assert.Equal("No value was supplied for '{{input.text}}'.", Assert.Single(result.Errors));
    }
}
