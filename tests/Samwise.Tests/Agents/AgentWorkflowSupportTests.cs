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
        var inboundEvent = new InboundEvent
        {
            Id = Guid.NewGuid().ToString(),
            Source = "Manual",
            ExternalId = "manual:test",
            Text = inboundText
        };
        var result = new IntakeResult(
            IntakeDecision.CreateWorkItem,
            null,
            "Revised customer offer",
            "AI-generated summary that must not replace the source text.",
            "This is actionable.",
            Receipt);

        var workItem = Samwise.Domain.WorkItems.WorkItem.CreateFromInbound(
            inboundEvent.Text,
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
            [new ToolCapability("documents/search", "search", "Search documents", "{\"type\":\"object\"}", true)]);

        var snapshot = AgentWorkflowSupport.CreatePlanToolSnapshot(catalog);

        Assert.Equal(catalog.CatalogVersion, snapshot.CatalogVersion);
        Assert.Equal(catalog.RawJson, snapshot.RawJson);
        var tool = Assert.Single(snapshot.Tools);
        Assert.Equal("documents/search", tool.Id);
        Assert.Equal("{\"type\":\"object\"}", tool.InputSchemaJson);
        Assert.True(tool.CanInvoke);
    }

    [Fact]
    public void PlanningValuesExposeConfiguredPlaybooksAsStructuredGuidance()
    {
        var now = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var workItem = Samwise.Domain.WorkItems.WorkItem.CreateFromInbound(
            "Process the attached invoice.",
            "Process invoice",
            now);
        var playbook = new Playbook
        {
            Id = "invoice-processing",
            Name = "Invoice processing",
            Description = "Check duplicates and create only a draft entry.",
            IsEnabled = true,
            UpdatedAt = now
        };
        var catalog = new ToolCatalogSnapshot("catalog", "{}", []);

        var values = AgentWorkflowSupport.BuildPlanningValues(
            workItem,
            [],
            null,
            [playbook],
            catalog,
            now);

        var renderedPlaybooks = Assert.IsType<string>(values["playbooks"]);
        Assert.Contains("invoice-processing", renderedPlaybooks, StringComparison.Ordinal);
        Assert.Contains("create only a draft entry", renderedPlaybooks, StringComparison.Ordinal);
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
}
