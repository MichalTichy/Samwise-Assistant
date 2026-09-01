# Final Codex CLI API Gateway contract used by Samwise

**Gateway baseline:** `D:\Work\CodexCLI-API-Gateway` at `29b3177` (2026-08-24)

## Integration decision

Samwise uses a stateless Microsoft Agent Framework `ChatClientAgent` over
`Microsoft.Extensions.AI.OpenAI` and the official OpenAI .NET Chat Completions client. The
provider-neutral `OpenAiAgentRuntime` points at the configured OpenAI-compatible base URL, which
is currently the Gateway. Only the rich `/tools` catalog and model-capability extensions remain
behind a Gateway-specific adapter. Samwise does not use the Responses API, native Codex sessions,
Agent Framework function tools, or hosted web search.

Pinned Samwise packages are:

- `Microsoft.Agents.AI` 1.17.0;
- `Microsoft.Extensions.AI` 10.9.0;
- `Microsoft.Extensions.AI.OpenAI` 10.9.0;
- `OpenAI` 2.12.0.

`Microsoft.Extensions.AI.OpenAI` 10.9.0 requires OpenAI `>= 2.12.0 && < 2.13.0`, so Samwise
cannot use the Gateway compatibility suite's independent OpenAI 2.13.0 pin without replacing or
upgrading that connector. Samwise's captured-wire test is therefore the authoritative consumer
fixture for this package set.

## Profiles and authorization

Samwise keeps three fixed server-side endpoint/credential profiles:

| Profile | Gateway scope | Credential | MCP behavior |
|---|---|---|---|
| Intake | projectless `/v1/` | planning key | no MCP tools |
| Planning | `/p/samwise-planning/v1/` | planning key | only tools safe for the planner to invoke, normally none or read-only |
| Execution | `/p/samwise-execution/v1/` | execution key | exact MCP tools allowed after plan approval |

Gateway API keys are created in the Gateway admin UI and granted to the corresponding projects.
The planning service fetches both catalogs: it records the planning catalog and embeds the
execution catalog in the planning prompt. The planning model therefore knows which tools an
approved execution may use without receiving the execution credential or execution project.

The model and reasoning effort are selected from the relevant nested stage in Samwise's persisted agent configuration
for each request. They are not coupled to the Gateway API key or project profile. Samwise reads each stage profile's
`GET /v1/models` catalog and stores the exact model and reasoning-effort strings selected from that model's advertised
`supported_reasoning_efforts`; it does not maintain a competing enum.

## Chat contract

Samwise sends non-streaming `POST chat/completions` calls with:

- `model`;
- one user text message containing the fully rendered prompt;
- `reasoning_effort`;
- `response_format.type = json_schema`;
- a strict JSON schema whose properties are all required.

The Gateway returns the standard non-streaming `chat.completion` envelope. Samwise parses the
assistant text as the requested structured result and records the completion ID, model, raw
assistant response, and rendered prompt.

`Microsoft.Extensions.AI.ReasoningEffort` is a closed enum and can lag behind endpoint values. The standard OpenAI runtime therefore sets
OpenAI.NET's extensible raw `ChatReasoningEffortLevel` on `ChatCompletionOptions`, while continuing to use
`ChatClientAgent` for execution and structured result handling.

The Gateway starts an independent `codex exec --ephemeral` run for every call. Samwise persists no
Agent Framework or Codex session. Each new classification, plan revision, or execution request
reconstructs its complete context from Samwise's canonical work item, comments, prior plan, feedback,
and current tool catalog.

Samwise must not register local `AITool` functions or enable Agent Framework hosted web search.
The finalized Gateway explicitly rejects request-level `tools`, function calls, tool-result
messages, and `web_search_options`. Gateway-managed MCP calls happen inside Codex and only the
final assistant content is returned.

## Gateway extension boundary

`POST /chat/completions`, its messages, model, reasoning effort, and strict response format are
standard OpenAI transport concerns implemented only in `Samwise.Infrastructure.AgentRuntimes`.
`Samwise.Infrastructure.CodexGateway` never executes an agent request. It implements
`IAgentToolCatalog` with the nonstandard versioned `/tools` endpoint and `IAgentModelCatalog` with
the Gateway's `supported_reasoning_efforts` and `default_reasoning_effort` extensions on `/models`.

## Tool catalog contract

Samwise calls:

```text
GET /p/{projectId}/v1/tools
Authorization: Bearer <profile key>
```

The optional `?detail=full` alias remains valid, but the canonical endpoint already returns the
complete catalog. Its envelope is:

```json
{
  "object": "list",
  "catalog_version": "sha256:<opaque deterministic hash>",
  "data": [
    {
      "id": "server-id",
      "name": "Server name",
      "version": "1.0.0",
      "required": false,
      "tools": [
        {
          "id": "server-id/tool-name",
          "name": "tool-name",
          "description": "What the tool does",
          "input_schema": { "type": "object" }
        }
      ]
    }
  ]
}
```

The finalized Gateway deliberately returns only tools enabled for the authenticated key/project.
There is no `can_invoke` property and no separate visible-only grant. Samwise consequently treats
every returned tool as invocable in that profile. A plan is valid only when every referenced tool
ID appears in the execution catalog.

The plan stores the exact execution `catalog_version`. Immediately before execution, Samwise
fetches the execution catalog again and fails closed if its version changed after approval.

## Failure and retry policy

Intake and planning clients use bounded SDK retries because their credentials must not have
side-effecting tools. The execution client disables SDK retries. Once an execution call has
started, a cancellation, timeout, transport failure, or lost response is treated as
`OutcomeUnknown`; Samwise does not automatically repeat it.

Gateway errors use the OpenAI error envelope. Unsupported semantic request fields return an
explicit `400 invalid_request_error`; authentication and project grants are enforced before the
endpoint use case runs.

## Compatibility verification

Samwise tests capture the actual request emitted by its pinned Agent Framework/OpenAI connector
and assert:

- project/base URL composition and Bearer authentication;
- non-streaming structured output;
- `reasoning_effort`;
- `response_format.json_schema.strict = true`;
- snake-case schema properties and required fields;
- parsing of the final tool catalog without the removed `can_invoke` member.

The Gateway repository separately tests its official OpenAI 2.13.0 client, strict JSON-schema
contract, unsupported local tools, streaming, project authorization, MCP discovery, and full
HTTP pipeline. Upgrading any of Samwise's four connector packages is a wire-contract change and
requires reviewing Samwise's captured request plus the Gateway compatibility fixtures.
