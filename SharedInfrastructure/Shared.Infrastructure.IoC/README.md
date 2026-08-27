# Shared.Infrastructure.IoC

This project provides the assembly-discovered installer convention used by the Samwise composition root. It keeps `Program.cs` small while allowing each infrastructure or logic project to register its own services.

## Main components

- `IInstaller` defines the registration contract. `IHighPriorityInstaller` and `ILowPriorityInstaller` control broad registration order.
- `InstallerDiscovery` loads Samwise and shared-infrastructure assemblies, discovers concrete installers, and invokes them in priority order.
- `FullAssemblyLoader` makes referenced assemblies visible before discovery.
- `ServiceCollectionExtensions` contains checked registration helpers that reject accidental duplicate registrations.

## Registration flow

1. `Samwise.Web` calls `InstallerDiscovery.RunInstallersFromAllReferencedAssemblies` during startup.
2. Matching `Samwise.*` and `Shared.Infrastructure.*` assemblies are loaded.
3. Discovered installers run in high, normal, then low priority order.
4. Each project registers only its own concern.

## Boundary

This project owns discovery and registration mechanics only. It must not know about Samwise domain types, workflows, persistence documents, or UI components.
