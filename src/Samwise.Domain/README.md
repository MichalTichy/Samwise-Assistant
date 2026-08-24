# Samwise.Domain

This project contains Samwise's durable business state and transition rules. It is persistence-aware only through minimal document identity/version contracts and has no dependency on Blazor, MediatR, Agent Framework, or the Codex Gateway.

## Structure and main components

- `WorkItems/WorkItem` is the main consistency boundary. It owns plans, comments, discard metadata, revision changes, and lifecycle transitions.
- `WorkItems/WorkItemPlan` is the serializable plan snapshot presented to the user. It includes steps, tool-catalog snapshots, assumptions, risks, the work-item revision for which it was created, and its optional review.
- `WorkItems/WorkItemComment` stores user, Samwise, and system context inside the aggregate.
- `WorkItems/PlanReview` records the single approval or rejection outcome owned by its plan.
- `WorkItems/WorkItemStatus` defines the draft, approval, execution, completion, attention, uncertainty, and discarded states.
- `Inbox/InboundEvent` is the source-neutral audit document for manual input and future connectors.
- `Agents/AgentInvocation` records one intake, planning, or execution call and its final outcome.
- `Prompts/PromptTemplate` stores the currently configured prompt for one workflow stage.

## Work-item lifecycle

1. Actionable intake creates a work item or appends context to an active one.
2. New context increments the revision and invalidates the previous plan.
3. Planning attaches a plan for the exact current revision and moves the work item to approval.
4. Approval succeeds only for the exact plan ID and revision shown to the user.
5. Execution moves through `Executing` to `Completed`, `NeedsAttention`, or `OutcomeUnknown`.
6. A potentially interrupted side-effecting execution is never silently retried.

## Aggregate boundary

Plans, their reviews, plan steps, tool snapshots, comments, and discard metadata are nested in `WorkItem` because they change with and are consistent with that work item. Approval and rejection belong to the reviewed plan; discard belongs to the work-item lifecycle. Inbound events, prompt templates, and agent invocations are independent documents because they have separate identity, retention, and access patterns.

## Rules for this project

- Put state transitions and invariants on domain types, not in pages or persistence adapters.
- Keep external SDK, HTTP, database-session, and dependency-injection code out.
- Do not add compatibility aliases for pre-release model names; V1 evolves the model directly.
