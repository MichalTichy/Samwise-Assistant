using Samwise.Logic.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.Playbooks;

namespace Samwise.Tests.Agents;

public sealed class AgentWorkflowSupportTests
{
    private static readonly AgentReceipt Receipt = new(
        "Test",
        "test-model",
        "test-completion",
        "{}");

    [Fact]
    public void NewWorkItemPreservesInboundTextInsteadOfAiDescription()
    {
        const string inboundText = "Prepare the revised offer.\nInclude the signed appendix and delivery dates.";
        var inboundEvent = new ManualInboundEvent
        {
            Id = Guid.NewGuid().ToString(),
            ManualInputId = "manual:test",
            Input = inboundText
        };
        var result = new IntakeResult(
            IntakeDecision.CreateWorkItem,
            null,
            "Revised customer offer",
            "AI-generated summary that must not replace the source text.",
            "This is actionable.",
            Receipt);

        var workItem = Samwise.Domain.WorkItems.WorkItem.CreateFromInbound(
            inboundEvent.GetIntakeText(),
            result.ProposedTitle,
            new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal("Revised customer offer", workItem.Title);
        Assert.Equal(inboundText, workItem.Description);
        Assert.NotEqual(result.ProposedDescription, workItem.Description);
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

        var body = AgentWorkflowSupport.FormatExecutionResult(result);

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

    [Fact]
    public void PlanToolSnapshotPreservesTheExactStructuredCatalog()
    {
        var catalog = new ToolCatalogSnapshot(
            "sha256:catalog",
            "{\"catalog_version\":\"sha256:catalog\"}",
            [new ToolServerCapability(
                "documents",
                "Documents",
                "1.0.0",
                false,
                null,
                null,
                null,
                [new ToolCapability(
                    "documents/search",
                    "search",
                    "Search documents",
                    "{\"type\":\"object\"}",
                    true)])]);

        var snapshot = AgentWorkflowSupport.CreatePlanToolSnapshot(catalog);

        Assert.Equal(catalog.CatalogVersion, snapshot.CatalogVersion);
        Assert.Equal(catalog.RawJson, snapshot.RawJson);
        var tool = Assert.Single(snapshot.Tools);
        Assert.Equal("documents/search", tool.Id);
        Assert.Equal("{\"type\":\"object\"}", tool.InputSchemaJson);
        Assert.True(tool.CanInvoke);
    }

    [Fact]
    public void SelectedPlaybookIsSnapshottedForThePlan()
    {
        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var playbook = new Playbook
        {
            Id = "invoice-processing",
            Name = "Invoice processing",
            Description = "Create only a draft entry.",
            IsEnabled = true,
            UpdatedAt = now
        };

        var snapshot = AgentWorkflowSupport.ResolvePlaybookSnapshot(playbook.Id, [playbook]);

        Assert.NotNull(snapshot);
        Assert.Equal(playbook.Id, snapshot.Id);
        Assert.Equal(playbook.Name, snapshot.Name);
        Assert.Equal(playbook.Description, snapshot.Description);
        Assert.Equal(playbook.UpdatedAt, snapshot.UpdatedAt);
    }

    [Fact]
    public void UnknownPlaybookSelectionIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AgentWorkflowSupport.ResolvePlaybookSnapshot("invented", []));

        Assert.Contains("not currently enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanValidationAcceptsSuggestedJsonParameters()
    {
        var plan = new PlanDraft(
            null,
            "Create the draft.",
            [],
            [],
            [new PlanStepDraft(
                1,
                "Create draft",
                "Create the approved draft.",
                [new PlanToolCallDraft("documents/create", "{\"title\":\"Approved draft\"}")],
                true,
                "The draft ID is returned.")]);
        var catalog = new ToolCatalogSnapshot(
            "catalog",
            "{}",
            [new ToolServerCapability(
                "documents",
                "Documents",
                "1.0.0",
                false,
                null,
                null,
                null,
                [new ToolCapability("documents/create", "create", null, "{}", true)])]);

        AgentWorkflowSupport.ValidatePlan(plan, catalog);
    }

    [Fact]
    public void PlanValidationRejectsInvalidSuggestedParameters()
    {
        var plan = new PlanDraft(
            null,
            "Create the draft.",
            [],
            [],
            [new PlanStepDraft(
                1,
                "Create draft",
                "Create the approved draft.",
                [new PlanToolCallDraft("documents/create", "not JSON")],
                true,
                "The draft ID is returned.")]);
        var catalog = new ToolCatalogSnapshot(
            "catalog",
            "{}",
            [new ToolServerCapability(
                "documents",
                "Documents",
                "1.0.0",
                false,
                null,
                null,
                null,
                [new ToolCapability("documents/create", "create", null, "{}", true)])]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => AgentWorkflowSupport.ValidatePlan(plan, catalog));

        Assert.Contains("not valid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanningQuestionRequiresDistinctChoices()
    {
        var question = new PlanningQuestionDraft(
            "Which outcome do you prefer?",
            null,
            [
                new PlanningQuestionOptionDraft("First", null),
                new PlanningQuestionOptionDraft("first", "Duplicate label")
            ]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => AgentWorkflowSupport.ValidatePlanningQuestion(question));

        Assert.Contains("distinct", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
