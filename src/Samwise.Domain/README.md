# Samwise.Domain

This project contains Samwise's durable business state and transition rules. It is persistence-aware only through minimal document identity/version contracts and has no dependency on Blazor, MediatR, Agent Framework, or the Codex Gateway.

## Structure and main components

- `WorkItems/WorkItem` is the main consistency boundary. It owns plans, comments, pending planning questions, user planning decisions, structured execution or planning-resolution details, discard metadata, revision changes, and lifecycle transitions.
- `WorkItems/WorkItemPlan` is the serializable plan snapshot presented to the user. It includes steps, decisions, an optional selected-playbook snapshot, tool-catalog snapshots, assumptions, risks, the work-item revision, its optional automatic-approval assessment, and its review.
- `WorkItems/WorkItemComment` stores user, Samwise, and system context inside the aggregate.
- `WorkItems/PlanReview` records the single approval or rejection outcome owned by its plan.
- `WorkItems/WorkItemStatus` defines the draft, approval, execution, completion, attention, uncertainty, and discarded states.
- `Inbox/InboundEvent` is the source-neutral processing lifecycle shared by every input observed by Samwise. It embeds the zero or more `InboundEventOutcome` records that link the input to every Work Item it created or updated, along with trusted source guidance and structured historical context. The Domain project owns only this base event contract and its common nested state; each input feature owns its concrete event documents and source-specific invariants.
- `Agents/AgentInvocation` records one intake, planning, or execution call, including its rendered prompt, model settings, exact available-tool snapshot, raw response, timing, and final outcome.
- `Agents/AgentConfiguration` is the single current-settings document. It owns fixed nested Intake, Planning, Execution, AutomaticApproval, and AutoApprovedExecution settings, each with its model ID, exact Gateway reasoning-effort ID, prompt, and update timestamp. Reasoning effort is deliberately not an enum because supported values belong to the selected model.
- `Playbooks/Playbook` stores one reusable, optionally enabled intake-and-planning instruction configured by the user.

## Work-item lifecycle

1. Intake can create or update multiple Work Items when one input contains independently achievable outcomes. All changes are applied together before each affected Work Item is planned once.
2. New context increments the revision and invalidates the previous plan.
3. Planning either attaches a plan for the exact current revision, asks one structured user question, completes an outcome already achieved outside Samwise, or discards work proven no longer relevant. Terminal planning outcomes retain their reason and invocation ID.
4. A planning question moves the Work Item to `AwaitingUserInput`. Its mutually exclusive choices plus the UI-provided Other answer are stored as a structured decision; answering advances the revision and immediately replans. The user may also request a fresh plan without answering when external state or time may have made the question stale.
5. Approval succeeds only for the exact plan ID and revision shown to the user. An eligible playbook may instead trigger automatic assessment; a refusal is stored on the plan and leaves human review available.
6. Execution moves through `Executing` to `Completed`, `NeedsAttention`, or `OutcomeUnknown`.
7. A potentially interrupted side-effecting execution is never silently retried. The user can explicitly retry the same approved plan after checking the affected systems and accepting the duplicate-action risk.
8. A discarded Work Item can be explicitly restored. Restoration retains its plan and discard history, advances the revision, and starts fresh planning.

## Aggregate boundary

Plans, planning questions and decisions, their selected-playbook snapshots, automatic-approval assessments and reviews, numbered plan steps, suggested tool calls and JSON parameters, tool snapshots, comments, source references, completion result, and discard metadata are nested in `WorkItem` because they change with and are consistent with that work item. A source reference preserves the originating inbound-event ID and external locator without copying large source content. Intake outcomes are nested in their `InboundEvent` because they form the audit result of processing one observed input. The five agent-stage settings are nested in one `AgentConfiguration` because they form one fixed configuration edited and loaded together. Inbound events, Work Items, playbook definitions, that configuration root, and agent invocations remain separate documents because they have separate identity, retention, and access patterns.

## Rules for this project

- Put state transitions and invariants on domain types, not in pages or persistence adapters.
- Keep external SDK, HTTP, database-session, and dependency-injection code out.
- Do not add compatibility aliases for pre-release model names; V1 evolves the model directly.
- Keep concrete input-source events, configuration, and processing rules in their `Samwise.Inputs.*` feature modules. Domain owns only the source-neutral inbound lifecycle.
