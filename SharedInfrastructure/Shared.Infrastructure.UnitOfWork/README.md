# Shared.Infrastructure.UnitOfWork

This project defines a small isolation seam for code that may need to escape an ambient unit of work. The default Samwise registration performs the workload directly.

## Main components

- `IUnitOfWorkExecutor` defines asynchronous and synchronous isolation operations.
- `NoUnitOfWorkExecutor` executes the supplied delegate without creating another persistence scope.
- `UnitOfWorkExecutorInstaller` registers the fallback at low priority.

## Flow

1. A component requests `IUnitOfWorkExecutor` when it needs an explicit isolation boundary.
2. In V1, `NoUnitOfWorkExecutor` invokes the workload immediately.
3. A persistence-specific implementation may replace the fallback if real nested isolation becomes necessary.

## Boundary

This abstraction does not commit Marten sessions. The Marten-specific ambient unit-of-work implementation lives in `Shared.Infrastructure.Persistence.Marten`.
