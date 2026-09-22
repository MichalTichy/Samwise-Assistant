using Samwise.Domain.Agents;

namespace Samwise.Logic.Prompts;

public static class AgentPromptPlaceholders
{
    [PromptPlaceholder(
        "Input text",
        "Normalized text received from the source.",
        "Please send the revised offer.",
        AgentStage.Intake)]
    public const string InputText = "input.text";

    [PromptPlaceholder(
        "Source guidance",
        "Trusted user-configured guidance captured for the input source.",
        "This is the user's work mailbox...",
        AgentStage.Intake)]
    public const string InputSourceGuidance = "input.source_guidance";

    [PromptPlaceholder(
        "Input context",
        "Prior source items supplied only to interpret the new input, as JSON.",
        "[{...}]",
        AgentStage.Intake)]
    public const string InputContext = "input.context";

    [PromptPlaceholder(
        "Active work items",
        "Compact JSON list of possible existing work items.",
        "[{...}]",
        AgentStage.Intake)]
    public const string ActiveWorkItems = "active_work_items";

    [PromptPlaceholder(
        "Planning tools",
        "Compact planning Gateway MCP capability catalogue as untrusted JSON data.",
        "{...}",
        AgentStage.Intake)]
    public const string PlanningTools = "planning_tools";

    [PromptPlaceholder(
        "Execution tools",
        "Execution tool capabilities: compact catalogue for intake and planning when schema lookup is available; execution uses Gateway-provided MCP tools.",
        "{...}",
        AgentStage.Intake,
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string ExecutionTools = "execution_tools";

    [PromptPlaceholder(
        "Current time",
        "Current UTC time in ISO 8601 format.",
        "2026-08-17T14:30:00Z",
        AgentStage.Intake,
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string CurrentTime = "current_time";

    [PromptPlaceholder(
        "Work item ID",
        "Stable Samwise work item identifier.",
        "8b07...",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemId = "work_item.id";

    [PromptPlaceholder(
        "Work item title",
        "Short work item title.",
        "Prepare revised offer",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemTitle = "work_item.title";

    [PromptPlaceholder(
        "Work item description",
        "The complete current work item statement.",
        "Prepare the offer...",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemDescription = "work_item.description";

    [PromptPlaceholder(
        "Work item revision",
        "Revision the generated or approved plan must match.",
        "2",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemRevision = "work_item.revision";

    [PromptPlaceholder(
        "Work item comments",
        "User context and planning feedback as Markdown.",
        "- Add delivery dates",
        AgentStage.Planning,
        AgentStage.Execution)]
    public const string WorkItemComments = "work_item.comments";

    [PromptPlaceholder(
        "Previous plan",
        "Previous plan JSON, or an empty string.",
        "{...}",
        AgentStage.Planning)]
    public const string PreviousPlan = "previous_plan";

    [PromptPlaceholder(
        "Rejection feedback",
        "Latest user feedback, or an empty string.",
        "Use the new price list",
        AgentStage.Planning)]
    public const string RejectionFeedback = "rejection_feedback";

    [PromptPlaceholder(
        "Playbooks",
        "Enabled playbook summaries in JSON; planning can retrieve full guidance when the Samwise lookup tool is available.",
        "[{...}]",
        AgentStage.Intake,
        AgentStage.Planning)]
    public const string Playbooks = "playbooks";

    [PromptPlaceholder(
        "Approved plan",
        "Execution projection of the exact approved plan, excluding stored tool catalog snapshots and audit metadata.",
        "{...}",
        AgentStage.Execution)]
    public const string ApprovedPlan = "approved_plan";
}
