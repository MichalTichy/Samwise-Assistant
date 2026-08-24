# Samwise.Logic

This project is Samwise's application layer. It defines external operations, mandatory workflow coordination, AI runtime contracts, prompt rendering, and named read specifications while remaining independent of the concrete UI, Gateway transport, and Marten mappings.

## Structure

Each feature is grouped by responsibility:

- `Usecases/` contains externally triggered operations only: a user command, endpoint request, or newly received input event.
- `Services/` contains reusable processing and mandatory follow-up flow shared by use cases.
- `Specifications/` contains named read definitions executed through `IReadOnlyRepository<T>`.
- `Initialization/` contains feature-owned startup seeders.
- `Agents/` contains the `IAgentRuntime` port, structured request/result models, invocation tracking, and prompt-context assembly.
- `Installers/LogicInstaller` registers MediatR and application services.

There are intentionally no read-only `Get*Usecase` wrappers. Blazor pages call the read-only repository with public specifications directly.

## Intake and planning flow

1. `CaptureInboundEventUsecase` validates and deduplicates an external input, then stores an `InboundEvent` in `Pending` state.
2. It calls `InboundEventProcessingService`, the reusable flow also used by retry.
3. `InboundEventClassificationService` renders the intake prompt, records an invocation, calls `IAgentRuntime.ClassifyAsync`, and commits the intake decision.
4. `Ignore` and `NeedsReview` finish without a work item.
5. `CreateWorkItem` or `AppendToWorkItem` returns the actionable work-item ID.
6. `InboundEventProcessingService` calls `WorkItemPlanningService` for that work item.
7. Planning fetches both planning and execution tool catalogs, renders the complete planning prompt, validates referenced execution tools, attaches the plan, and leaves it awaiting approval.

## Approval and execution flow

1. Context addition, plan rejection, and planning retry are external use cases. Each changes the aggregate and calls `WorkItemPlanningService` directly because replanning is an integral part of the requested operation.
2. `ApproveWorkItemPlanUsecase` performs revision-bound approval and calls `WorkItemExecutionService` directly.
3. Execution reloads the execution tool catalog and fails closed if it differs from the approved snapshot.
4. A confirmed response completes or marks the work item for attention. An uncertain interrupted execution becomes `OutcomeUnknown` and is not automatically repeated.

## Shared services

- `AgentInvocationService` owns the start/complete/fail lifecycle used by intake, planning, and execution.
- `PromptRenderer` validates placeholders and renders the full prompt stored with every invocation.
- `AgentWorkflowSupport` assembles deterministic structured context for each stage.

## Architectural rules

- A use case never sends another use case.
- Mandatory continuation uses direct service calls, not notifications.
- Notifications are reserved for independent reactions after an operation finishes; V1 currently has none.
- Code shared by multiple external operations belongs in a service.
- Reads use specifications and `IReadOnlyRepository<T>`; mutations use domain behavior through `IRepository<T>`.
- Concrete Gateway, runtime selection, and persistence behavior belong in infrastructure projects.
