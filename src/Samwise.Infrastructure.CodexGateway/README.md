# Samwise.Infrastructure.CodexGateway

This project contains only Codex CLI API Gateway extensions that are unavailable in the standard OpenAI API. Ordinary agent inference is deliberately absent and belongs to `Samwise.Infrastructure.AgentRuntimes`.

## Main components

- `CodexGatewayCatalogClient` implements `IAgentToolCatalog` with the custom versioned `/tools` route and `IAgentModelCatalog` with the Gateway capability extensions on `/models`.
- `CodexGatewayInstaller` registers the two application ports over one catalog client and creates authenticated named HTTP clients for each stage.
- The client reuses provider-neutral `AgentEndpointOptions` from the runtime project, so URLs and credentials have one configuration shape.

## Model-catalog flow

1. The catalog client calls the selected stage profile's standard `GET /v1/models` endpoint.
2. It preserves each model ID, its `supported_reasoning_efforts`, and its `default_reasoning_effort`.
3. Only the latter two fields are Gateway extensions; the application uses them for model-specific UI options and command-side validation.

## Tool-catalog flow

1. The client calls the selected Intake, Planning, or Execution profile's `GET /v1/tools` endpoint with that profile's credential.
2. The Gateway returns only tools enabled for that key/project and a deterministic catalog version.
3. The transport preserves the raw JSON plus normalized server and tool metadata for the settings UI, prompt context, invocation history, and approval checks.

## Security and reliability boundaries

- Intake and planning may share planning credentials; execution uses its separate credential and project.
- The browser never receives Gateway keys or calls this project directly.
- This project never creates an OpenAI client or executes an agent request.
- This project does not decide when to classify, plan, approve, or execute; that belongs to `Samwise.Logic` and `Samwise.Infrastructure.AgentRuntimes`.
