# Samwise.Tests

This project verifies domain invariants, application workflow boundaries, Gateway compatibility, runtime translation, prompt behavior, and the Marten document shape. Tests are organized by the production concern they protect.

## Structure

- `WorkItems/` tests aggregate transitions, revision-bound approval, invalidation, discard restoration, execution outcomes, and terminal planning dispositions.
- `Inbox/` tests inbound-event state, computed manual/received-email/sent-email intake text, stable IMAP identity, and the classification-to-planning processing flow.
- `Inputs/Email/` tests polling configuration, exact ignored-sender normalization, and independent INBOX/Sent checkpoint initialization.
- `Prompts/` tests the single agent-configuration aggregate, full and prompt-only stage updates, attribute-derived placeholder metadata and stage assignments, template validation, deterministic rendering, and the centralized builder's repository/catalog retrieval for all three stages.
- `Agents/` tests invocation lifecycle, playbook steering/snapshots, per-stage model and effort propagation, standard OpenAI runtime registration and captured requests, plus the isolated Gateway catalog extensions.
- `Architecture/` protects installer composition and application-layer conventions.
- `Persistence/` verifies the nested work-item and agent-configuration document shapes plus work-item optimistic concurrency against real PostgreSQL when enabled.
- `Identity/` verifies request-scheme cookie behavior and the explicit strict-HTTPS deployment option.
- `Mcp/` verifies the exact tool surface, API-key authentication, compact read results, and complete source-specific detail exposed to agents.

## Architectural guards

`LogicInstallerTests` intentionally fails if:

- a use-case handler injects `ISender` or another sender implementation and can therefore dispatch a nested use case;
- a read-only `Get*Usecase` wrapper is introduced instead of a specification;
- required workflow services are missing from dependency injection.

These tests turn the project READMEs' application-layer rules into executable constraints.

## Agent infrastructure tests

Runtime tests capture local OpenAI Chat Completions traffic and verify endpoint composition, authentication, strict structured output, raw model-specific reasoning efforts including `max`, schema property naming, and response parsing. Separate Gateway catalog tests verify only the model-capability extensions and finalized versioned tool-catalog contract. No real AI is required for these tests.

## PostgreSQL test

`WorkItemDocumentPersistenceTests` runs only when `SAMWISE_TEST_POSTGRES` contains a disposable PostgreSQL connection string. It stores a complete nested work-item graph, confirms that a stale document update throws a Marten concurrency exception, verifies that all three agent stages round-trip inside one configuration document, and verifies that manual/email inbound subtypes round-trip through the common Marten repository.

## Running the suite

```powershell
dotnet test tests/Samwise.Tests/Samwise.Tests.csproj --no-restore
```

To include the real persistence test:

```powershell
$env:SAMWISE_TEST_POSTGRES = "Host=localhost;Port=5432;Database=samwise_tests;Username=postgres;Password=postgres"
dotnet test tests/Samwise.Tests/Samwise.Tests.csproj --no-restore
```

Use a disposable database: the persistence test creates schema objects and test documents.
