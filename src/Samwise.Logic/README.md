# Samwise.Logic

This project is Samwise's application layer. It defines external operations, mandatory workflow coordination, AI runtime contracts, prompt rendering, and named read specifications while remaining independent of the concrete UI, Gateway transport, and Marten mappings.

## Structure

Each feature is grouped by responsibility:

- `Usecases/` contains externally triggered operations only: a user command, endpoint request, or newly received input event.
- `Services/` contains reusable processing and mandatory follow-up flow shared by use cases.
- `Specifications/` contains named read definitions executed through `IReadOnlyRepository<T>`.
- `Initialization/` contains feature-owned startup seeders.
- `Agents/` contains separate `IAgentRuntime`, `IAgentModelCatalog`, and `IAgentToolCatalog` ports, stage-oriented request/result models under `Models/Intake`, `Models/Planning`, and `Models/Execution`, shared run/catalog models under `Models/Shared`, and invocation tracking.
- `Prompts/` contains the attribute-driven placeholder catalogue, template validation/rendering, the single `AgentPromptBuilder` that retrieves and assembles every stage's prompt context, the startup initializer for the agent-configuration document, the UI-triggered full stage-settings use case, and the MCP-triggered prompt-only use case.
- `Playbooks/` contains the UI- or MCP-triggered save use case and public read specification for reusable intake-and-planning guidance.
- `Inbox/` owns only the source-neutral capture, deduplication, classification, and processing pipeline. Concrete event documents and their source-specific operations live in independently composed `Samwise.Inputs.*` modules.
- `Installers/LogicInstaller` registers MediatR and application services.

Top-level types live in separate files named after the type. Related models are grouped in feature-local `Models/` folders, prompt placeholder types in `Prompts/Placeholders/`, and service contracts beside their implementations. A type may share a file only when it is a private or nested implementation detail used exclusively by the containing type.

There are intentionally no read-only `Get*Usecase` wrappers. Blazor pages call the read-only repository with public specifications directly.

The fixed Intake, Planning, and Execution settings form one `AgentConfiguration` document. Startup creates the complete aggregate; workflow services load that root and select the required nested stage. Model and reasoning-effort IDs are stored as strings because `IAgentModelCatalog` reports a model-specific set of supported efforts. `SaveAgentStageConfigurationUsecase` requires the root to exist, validates that pair against the current catalog, and mutates only the selected stage. `SaveAgentPromptUsecase` is the narrower MCP command: it validates and replaces only the prompt while preserving that stage's model and effort.

## Intake and planning flow

1. A source module creates its concrete `InboundEvent` and submits it through the public `IInboundEventCaptureService` port.
2. `InboundEventCaptureService` validates and deduplicates inputs, persists each event, and queues pending events without waiting for AI. A single hosted worker processes queued events sequentially and recovers pending events after a restart.
3. `InboundEventClassificationService` asks `AgentPromptBuilder` for a ready Intake prompt. The builder retrieves the new input, its trusted source-guidance snapshot, separately labelled historical context, active work items, enabled playbooks, configuration, time, and all three tool catalogs. Historical context can explain the trigger but cannot independently create duplicate work. Intake uses playbooks to recognize actionability but does not select one. The service records the Intake catalog with the invocation and calls `IAgentRuntime.ClassifyAsync`. Work Items retain structured source references; planning and execution prompts always receive those references so an agent can explicitly retrieve source content when needed.
4. Intake returns one overall decision plus zero or more independent Work Item changes. Ignore and NeedsReview contain no changes; ApplyChanges contains one or more creates or updates with scoped relevant text. Validation rejects partial, contradictory, duplicate-target, or stale-target results before anything is written.
5. All Work Item changes and the event's embedded outcomes commit together. New Work Items use the scoped relevant text rather than copying the complete source input into every result.
6. `InboundEventProcessingService` calls `WorkItemPlanningService` once for every distinct affected Work Item.
7. Planning asks `AgentPromptBuilder` for a ready prompt. The builder retrieves the work item, enabled playbooks, configuration, time, and both required catalogs before the configured planning model is called through `IAgentRuntime`.
8. Planning returns an explicit disposition. `PlanRequired` validates and snapshots the selected playbook, decisions, and referenced execution tools before leaving the work item awaiting approval. `UserInputRequired` validates and stores one question with two to six mutually exclusive choices after tools and playbooks cannot resolve a material preference. `AlreadyResolved` completes work proven finished by new context; `NoLongerRelevant` discards work proven unnecessary. Both terminal outcomes retain the planner's reason and invocation ID without fabricating an empty plan.

## Approval and execution flow

1. Context addition, planning-question answer, plan rejection, and planning retry are external use cases. Each changes the aggregate and calls `WorkItemPlanningService` directly because replanning is an integral part of the requested operation. A question answer is retained as structured context and the resulting plan snapshots the decisions it used.
2. `ApproveWorkItemPlanUsecase` performs revision-bound approval and calls `WorkItemExecutionService` directly.
3. Execution reloads the execution tool catalog and fails closed if it differs from the approved snapshot.
4. A confirmed successful response stores its summary, outputs, invocation ID, and completion time as a nested Work Item completion. A reported or interrupted uncertain execution becomes `OutcomeUnknown` and is not automatically repeated.
5. `RetryWorkItemExecutionUsecase` is the explicit user action that reruns the same approved plan from `OutcomeUnknown`; the previous uncertain invocation remains in the activity history.
6. `RestoreWorkItemUsecase` is the explicit user or MCP action that moves only a discarded Work Item into a new planning revision and calls `WorkItemPlanningService`; previous plans and discard metadata remain available.

## Shared services

- `AgentInvocationService` owns the start/complete/fail lifecycle used by intake, planning, and execution and snapshots the exact tool catalog available when every invocation starts. `AgentWorkflowSupport` validates multi-Work-Item intake responses as well as plans and planning questions.
- `AgentPromptPlaceholders` declares every placeholder key once as a constant with its label, description, example, and applicable stages in `PromptPlaceholderAttribute`. The settings UI, builder, and renderer all consume that catalogue.
- `AgentPromptBuilder` is the only component that renders runtime prompts. Workflow services pass identifiers rather than placeholder values; the builder retrieves each stage's data, uses catalogue constants for its value maps, and returns the rendered prompt, run configuration, and exact catalogs or playbooks required for validation and snapshots.
- `PromptRenderer` validates templates against the catalogue and performs the final token replacement. `AgentWorkflowSupport` contains plan validation and snapshot helpers only.

## Architectural rules

- A use case never sends another use case.
- Mandatory continuation uses direct service calls, not notifications.
- Notifications are reserved for independent reactions after an operation finishes; V1 currently has none.
- Code shared by multiple external operations belongs in a service.
- Reads use specifications and `IReadOnlyRepository<T>`; mutations use domain behavior through `IRepository<T>`.
- Standard OpenAI execution, Gateway-specific catalog discovery, runtime selection, and persistence behavior belong in infrastructure projects.
- Core application code must not reference a concrete input module. Each `Samwise.Inputs.*` project owns its event types, configuration, use cases, specifications, initialization, and adapter ports.
- Keep one top-level class, record, interface, enum, or struct per file; use folders rather than grab-bag source files for organization.
