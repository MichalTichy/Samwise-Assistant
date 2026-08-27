# Shared.Infrastructure.Initializer

This project provides ordered application-startup initialization. Samwise uses it for database migration, owner-account creation, and prompt seeding after dependency injection has been composed.

## Main components

- `IInitializer` defines a named initialization step, trigger, priority, and leader-only flag.
- `InitializerBase` prevents the same initializer instance from running twice and provides the implementation template.
- `InitializerTrigger` separates startup work from work that requires the application to be ready.
- `InitializerManager` selects, orders, and executes initializers for a trigger.
- `AddAllInitializers` discovers concrete initializers from loaded assemblies.
- `InitializerInstaller` registers the manager and supporting services.

## Startup flow

1. `Samwise.Web` discovers all `IInitializer` implementations during service registration.
2. After building the app, it runs the `OnStartup` trigger.
3. The manager checks leader status, filters applicable initializers, and orders them by trigger and priority.
4. Each initializer runs once; failures stop startup instead of leaving a partially initialized application hidden.
5. After the server starts, `OnApplicationReady` initializers run.

## Boundary

Feature-specific initialization belongs in the owning project. This shared project owns scheduling and lifecycle rules only.
