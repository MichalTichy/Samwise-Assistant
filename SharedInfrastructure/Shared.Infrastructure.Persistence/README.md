# Shared.Infrastructure.Persistence

This project defines persistence-neutral repository and specification contracts. It lets Samwise logic and Blazor read paths depend on stable abstractions without referencing Marten.

## Main components

- `IItemWithId` is the minimum document identity contract.
- `IReadOnlyRepository<T>` supports ID lookups, lists, counts, and specification execution.
- `IRepository<T>` adds document mutations.
- `ISpecification<TItem, TOut>` describes a single-result read.
- `IListSpecification<TItem, TOut>` describes a multi-result read.

## Read flow

1. A Blazor page or logic service creates a named specification from `Samwise.Logic`.
2. It passes that specification to `IReadOnlyRepository<T>`.
3. The configured persistence adapter supplies an `IQueryable<T>` and lets the specification apply filtering, ordering, limits, and optional projection.
4. The caller receives domain documents or a specified result type without knowing Marten APIs.

## Write flow

1. A use case or workflow service obtains `IRepository<T>`.
2. It loads and changes a domain document through domain behavior.
3. The repository delegates storage and concurrency handling to the configured adapter.

## Boundary

Specifications describe reads; they do not perform commands or workflow orchestration. Provider-specific sessions and queries belong in the persistence adapter.
