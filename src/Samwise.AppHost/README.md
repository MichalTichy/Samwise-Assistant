# Samwise.AppHost

This .NET Aspire host is the local development orchestrator. It starts the PostgreSQL dependency and the Samwise web process with the correct connection reference and health dependency.

## Main components

- `Program.cs` declares the complete local topology.
- `postgres` is the PostgreSQL server resource with a persistent development volume.
- `samwise` is the database exposed to the web project through Aspire configuration.
- `samwise-web` is the `Samwise.Web` project with an external HTTP endpoint and `/health` check.

## Startup flow

1. Aspire starts PostgreSQL and creates or locates the `samwise` database.
2. It injects the database connection into `Samwise.Web`.
3. The web resource waits until the database is available.
4. Samwise startup installers and initializers configure schemas, seed agent-stage settings, and ensure the owner exists.

## Boundary

This project is development orchestration only. It contains no application configuration defaults, domain behavior, persistence mappings, or production deployment logic.
