# Samwise.Logic

This project is Samwise's application layer. It defines external operations, mandatory workflow coordination, AI runtime contracts, prompt rendering, and named read specifications while remaining independent of the concrete UI, Gateway transport, and Marten mappings.

## Structure

Each feature is grouped by responsibility:

- `Usecases/` contains externally triggered operations only: a user command, endpoint request, or newly received input event.
- `Services/` contains reusable processing and mandatory follow-up flow shared by use cases.
- `Specifications/` contains named read definitions executed through `IReadOnlyRepository<T>`.
- `Initialization/` contains feature-owned startup seeders.
- `Agents/` contains separate `IAgentRuntime`, `IAgentModelCatalog`, and `IAgentToolCatalog` ports, structured request/result models, invocation tracking, and prompt-context assembly.
- `Prompts/` contains prompt rendering, the startup initializer for the single agent-configuration document, and the UI-triggered use case that updates one nested stage.
- `Playbooks/` contains the UI-triggered save use case and public read specification for reusable planning guidance.
- `Inputs/Email/` contains the IMAP adapter contracts, email-account and settings commands, account reads, checkpoint-based automatic processing, manual unread processing, and the startup initializer for email settings.
- `Installers/LogicInstaller` registers MediatR and application services.

There are intentionally no read-only `Get*Usecase` wrappers. Blazor pages call the read-only repository with public specifications directly.

The fixed Intake, Planning, and Execution settings form one `AgentConfiguration` document. Startup creates the complete aggregate; workflow services load that root and select the required nested stage. Model and reasoning-effort IDs are stored as strings because `IAgentModelCatalog` reports a model-specific set of supported efforts. `SaveAgentStageConfigurationUsecase` requires the root to exist, validates that pair against the current catalog, and mutates only the selected stage, so normal commands never invent missing configuration structure.

## Intake and planning flow

1. `SubmitManualInputUsecase` creates a `ManualInboundEvent`. The timer-triggered `PollEmailAccountsUsecase` imports every message delivered after an account's checkpoint, while the user-triggered `ProcessUnreadEmailsUsecase` imports unread messages regardless of age; both create `EmailInboundEvent` documents through the IMAP port.
2. Both use `InboundEventCaptureService` to validate, deduplicate, persist, and continue pending inputs through `InboundEventProcessingService`.
3. `InboundEventClassificationService` renders the intake prompt, records an invocation, calls `IAgentRuntime.ClassifyAsync`, and commits the intake decision.
4. `Ignore` and `NeedsReview` finish without a work item.
5. `CreateWorkItem` or `AppendToWorkItem` returns the actionable work-item ID.
6. `InboundEventProcessingService` calls `WorkItemPlanningService` for that work item.
7. Planning loads enabled playbooks and both catalogs through `IAgentToolCatalog`, renders the complete planning prompt, and calls the configured planning model and reasoning effort through `IAgentRuntime`.
8. Samwise validates the selected playbook ID and referenced execution tools, snapshots any selected playbook into the plan, attaches it, and leaves the work item awaiting approval.

## Approval and execution flow

1. Context addition, plan rejection, and planning retry are external use cases. Each changes the aggregate and calls `WorkItemPlanningService` directly because replanning is an integral part of the requested operation.
2. `ApproveWorkItemPlanUsecase` performs revision-bound approval and calls `WorkItemExecutionService` directly.
3. Execution reloads the execution tool catalog and fails closed if it differs from the approved snapshot.
4. A confirmed response completes or marks the work item for attention. An uncertain interrupted execution becomes `OutcomeUnknown` and is not automatically repeated.

## Shared services

- `AgentInvocationService` owns the start/complete/fail lifecycle used by intake, planning, and execution.
- `EmailInputProcessingService` coordinates both email read modes, exact-address ignores, durable checkpoint updates, and shared inbound capture. A keyed in-process lock prevents overlapping checks of one account.
- `PromptRenderer` validates placeholders—including planning's `{{playbooks}}` value—and renders the full prompt stored with every invocation.
- `AgentWorkflowSupport` assembles deterministic structured context for each stage.

## Architectural rules

- A use case never sends another use case.
- Mandatory continuation uses direct service calls, not notifications.
- Notifications are reserved for independent reactions after an operation finishes; V1 currently has none.
- Code shared by multiple external operations belongs in a service.
- Reads use specifications and `IReadOnlyRepository<T>`; mutations use domain behavior through `IRepository<T>`.
- Standard OpenAI execution, Gateway-specific catalog discovery, runtime selection, and persistence behavior belong in infrastructure projects.
