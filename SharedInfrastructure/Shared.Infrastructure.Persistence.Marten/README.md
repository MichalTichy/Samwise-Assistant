# Shared.Infrastructure.Persistence.Marten

This project is the reusable Marten implementation of the shared repository and unit-of-work contracts. Samwise copied the required infrastructure into this repository so the application has no source reference to Edifio.

## Main components

- `MartenRepository<T>` and related repository classes implement shared read/write contracts over Marten sessions.
- `AppMartenSessionFactory` and the tenancy-aware factory variants create query and document sessions.
- `IStorageConfiguration` lets the consuming application register its documents, schema, indexes, and conventions.
- `UnitOfWorkProvider` exposes the ambient document session used to make related document changes atomic.
- `UnitOfWork` and `UnitOfWorkExecutor` manage commit and isolation behavior.
- `DatabaseMigrator` and migration contracts provide reusable schema migration mechanics.
- `AddMartenPostgresPersistence` wires the repositories, sessions, and Marten store into dependency injection.

## Repository flow

1. The Samwise Marten installer registers PostgreSQL, an application `IStorageConfiguration`, and this project's persistence services.
2. A repository obtains the appropriate Marten session from the session factory or ambient unit of work.
3. Read specifications execute against the session queryable.
4. Writes are staged in the document session.
5. An ambient `UnitOfWorkProvider` commits related writes together; otherwise repository infrastructure controls the normal session boundary.

## Samwise-specific choices

- Tenancy is disabled.
- The generic `DatabaseInitializer` is excluded because Samwise owns focused startup initialization.
- Document mappings and indexes do not belong here; they are defined in `Samwise.Infrastructure.Persistence.Marten`.

## Boundary

This project must remain reusable. It contains no Samwise domain rules, use cases, prompt behavior, or workflow services.
