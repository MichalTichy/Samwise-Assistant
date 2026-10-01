using Samwise.Domain.Agents;
using Samwise.Logic.Prompts;

namespace Samwise.Tests.Prompts;

public sealed class PromptRendererTests
{
    private readonly AgentPromptPlaceholderCatalog _catalog = new();
    private readonly PromptRenderer _renderer;

    public PromptRendererTests()
    {
        _renderer = new PromptRenderer(_catalog);
    }

    [Fact]
    public void PlaceholderCatalogDerivesMetadataAndStagesFromTheConstantDeclarations()
    {
        var executionTools = _catalog.GetForStage(AgentStage.Intake)
            .Single(placeholder => placeholder.Key == AgentPromptPlaceholders.ExecutionTools);

        Assert.Equal("Execution tools", executionTools.Label);
        Assert.Equal(
            [AgentStage.Intake, AgentStage.Planning, AgentStage.Execution, AgentStage.AutoApprovedExecution],
            executionTools.Stages);
        var playbooks = _catalog.GetForStage(AgentStage.Intake)
            .Single(placeholder => placeholder.Key == AgentPromptPlaceholders.Playbooks);
        Assert.Equal([AgentStage.Intake, AgentStage.Planning], playbooks.Stages);
        Assert.DoesNotContain(
            _catalog.GetForStage(AgentStage.Intake),
            placeholder => placeholder.Key == AgentPromptPlaceholders.WorkItemDescription);
        Assert.Contains(
            _catalog.GetForStage(AgentStage.Planning),
            placeholder => placeholder.Key == AgentPromptPlaceholders.WorkItemDescription);
    }

    [Fact]
    public void RenderReplacesKnownPlaceholdersIncludingWhitespaceAndEmptyValues()
    {
        const string template = "Input: {{ input.text }}\nWork items: {{active_work_items}}\nAt: {{current_time}}";
        var values = new Dictionary<string, string?>
        {
            [AgentPromptPlaceholders.InputText] = "Please prepare the offer.",
            [AgentPromptPlaceholders.ActiveWorkItems] = null,
            [AgentPromptPlaceholders.CurrentTime] = "2026-08-17T08:00:00Z"
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
    public void IntakeAndPlanningPromptsAcceptThePlaybooksPlaceholder()
    {
        var intakeErrors = _renderer.Validate(AgentStage.Intake, "Playbooks: {{playbooks}}");
        var planningErrors = _renderer.Validate(AgentStage.Planning, "Playbooks: {{playbooks}}");

        Assert.Empty(intakeErrors);
        Assert.Empty(planningErrors);
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
