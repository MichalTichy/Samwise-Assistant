# Samwise.Infrastructure.Persistence.Marten

This project applies Samwise-specific document mappings to the reusable shared Marten repository infrastructure. It is intentionally a thin adapter: no use cases, prompt logic, workers, or workflow services belong here.

## Main components

- `MartenInstaller` resolves the `samwise` PostgreSQL connection string, registers the Npgsql data source, supplies Samwise storage configuration, and enables the shared Marten repositories with tenancy disabled.
- `SamwiseStorageConfiguration` selects the `samwise` schema and registers documents, indexes, and uniqueness rules.

## Document layout

- `WorkItem` is one revisioned aggregate document containing plans, selected-playbook snapshots, plan reviews, plan steps, comments, discard metadata, and tool snapshots.
- `InboundEvent` is one polymorphic audit collection containing manual and email event documents, with received-time and unique deduplication-key indexes.
- `EmailAccount` is one document per IMAP account so frequently changing synchronization state does not rewrite global configuration.
- `EmailInputConfiguration` is one document containing the polling interval and nested ignored-sender addresses.
- `AgentInvocation` is an independent operational document indexed by work-item ID.
- `AgentConfiguration` is one current-settings document containing nested Intake, Planning, and Execution settings.
- `Playbook` is an independent user-managed planning-guidance document.

## Persistence flow

1. Installer discovery invokes `MartenInstaller` during web startup.
2. Shared Marten infrastructure builds the document store using `SamwiseStorageConfiguration`.
3. Logic services and Blazor read paths request shared repository interfaces.
4. Named specifications execute against Marten queries without callers referencing Marten.
5. Work-item revisioning rejects stale concurrent updates instead of silently overwriting nested state.
6. Intake classification uses the ambient unit of work when an inbound event and work item must change atomically.

## Boundary

Document ownership is decided in `Samwise.Domain`; query intent lives in `Samwise.Logic`; reusable repository mechanics live in `Shared.Infrastructure.Persistence.Marten`. This project contains only Samwise's binding between them.
