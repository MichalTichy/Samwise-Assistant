# Samwise.Inputs.Todoist

This is the complete application module for Todoist input. It owns the Todoist configuration, the task inbound-event document, reader transport models and port, processing and configuration use cases, the per-task event specification, locking, and initialization.

`Samwise.Infrastructure.Todoist` is the direct Todoist API v1 adapter and polling host for this module. The UI-managed configuration holds the API token, which the adapter protects with ASP.NET Data Protection. The module captures concrete events through the generic `IInboundEventCaptureService`; neither `Samwise.Domain` nor the generic workflow depends on Todoist.

## Behavior

- Every active task whose due instant falls between `now − OverdueDays` and `now + LeadHours` becomes one `TodoistTaskInboundEvent`.
- All-day tasks are due at the start of their day; floating times use the task timezone, then the Todoist account timezone.
- The external identity is the task ID plus its due value, so a reschedule or the next occurrence of a recurring task is a new input. Earlier occurrences of the same task are supplied as linked context so intake can append to the existing work item.
- Tasks in ignored projects or with an ignored label are recorded without an AI call.
- The connector never writes to Todoist. Completing or commenting on a task is an ordinary plan step that execution performs with the Gateway Todoist MCP tools, using the task ID from the work item's source reference.

## Structure

- `Authentication` contains the credential-protection port.
- `Configuration` contains the persisted input configuration.
- `Discovery` contains the project and label catalogue exposed to configuration UI.
- `Ingestion` contains the reader port, task and comment snapshots, processing result, and per-process lock.
- `InboundEvents` contains the task event document, its due value, comments, and specification.
- `Usecases/Configuration` and `Usecases/Processing` contain only externally triggered application operations.
- `Initialization` and `Installers` compose the module without leaking Todoist types into the generic Domain or Logic projects.
