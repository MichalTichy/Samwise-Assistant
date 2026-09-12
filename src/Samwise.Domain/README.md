# Samwise.Domain

This project contains Samwise's durable business state and transition rules. It is persistence-aware only through minimal document identity/version contracts and has no dependency on Blazor, MediatR, Agent Framework, or the Codex Gateway.

## Structure and main components

- `WorkItems/WorkItem` is the main consistency boundary. It owns plans, comments, discard metadata, revision changes, and lifecycle transitions.
- `WorkItems/WorkItemPlan` is the serializable plan snapshot presented to the user. It includes steps, an optional selected-playbook snapshot, tool-catalog snapshots, assumptions, risks, the work-item revision for which it was created, and its optional review.
- `WorkItems/WorkItemComment` stores user, Samwise, and system context inside the aggregate.
- `WorkItems/PlanReview` records the single approval or rejection outcome owned by its plan.
- `WorkItems/WorkItemStatus` defines the draft, approval, execution, completion, attention, uncertainty, and discarded states.
- `Inbox/InboundEvent` is the common handling lifecycle for every received input. `ManualInboundEvent` and `EmailInboundEvent` store source-specific immutable data and compute the text passed to intake instead of duplicating a generic text field.
- `Inputs/Email/EmailAccount` stores one IMAP connection, its encrypted credential value, initial-import boundary, enabled state, and last-check outcome.
- `Inputs/Email/EmailInputConfiguration` is the singleton email-input configuration containing the polling interval and nested exact-address ignore list.
- `Agents/AgentInvocation` records one intake, planning, or execution call and its final outcome.
- `Agents/AgentConfiguration` is the single current-settings document. It owns fixed nested Intake, Planning, and Execution settings, each with its model ID, exact Gateway reasoning-effort ID, prompt, and update timestamp. Reasoning effort is deliberately not an enum because supported values belong to the selected model.
- `Playbooks/Playbook` stores one reusable, optionally enabled planning instruction configured by the user.

## Work-item lifecycle

1. Actionable intake creates a work item or appends context to an active one.
2. New context increments the revision and invalidates the previous plan.
3. Planning attaches a plan for the exact current revision and moves the work item to approval.
4. Approval succeeds only for the exact plan ID and revision shown to the user.
5. Execution moves through `Executing` to `Completed`, `NeedsAttention`, or `OutcomeUnknown`.
6. A potentially interrupted side-effecting execution is never silently retried.

## Aggregate boundary

Plans, their selected-playbook snapshots, reviews, plan steps, tool snapshots, comments, and discard metadata are nested in `WorkItem` because they change with and are consistent with that work item. Approval and rejection belong to the reviewed plan; discard belongs to the work-item lifecycle. The three agent-stage settings are nested in one `AgentConfiguration` because they form one fixed configuration edited and loaded together. Inbound events, playbook definitions, that configuration root, and agent invocations remain separate documents because they have separate identity, retention, and access patterns.

## Rules for this project

- Put state transitions and invariants on domain types, not in pages or persistence adapters.
- Keep external SDK, HTTP, database-session, and dependency-injection code out.
- Do not add compatibility aliases for pre-release model names; V1 evolves the model directly.
