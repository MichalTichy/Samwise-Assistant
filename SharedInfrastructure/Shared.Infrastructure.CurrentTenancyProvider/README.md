# Shared.Infrastructure.CurrentTenancyProvider

This project supplies the tenancy abstraction expected by the copied repository infrastructure. Samwise is single-user and non-tenant-aware, so V1 deliberately uses the no-tenancy implementation.

## Main components

- `ICurrentTenancyProvider` exposes the current tenant identifier expected by persistence adapters.
- `CurrentTenancyProviderNoTenancy` always returns the shared `*DEFAULT*` tenant.
- `CurrentTenancyProviderInstaller` registers the default implementation at low priority so an application could replace it before the fallback runs.

## Flow

1. Repository infrastructure asks `ICurrentTenancyProvider` for a tenant when no explicit tenant was supplied.
2. Samwise receives the fixed no-tenancy identifier.
3. Marten operates as one non-partitioned personal data store.

## Boundary

This is compatibility infrastructure, not a signal that Samwise supports multiple users or tenants. No Samwise feature should add tenant-aware behavior in V1.
