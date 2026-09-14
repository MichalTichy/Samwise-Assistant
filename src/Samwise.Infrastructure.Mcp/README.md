# Samwise.Infrastructure.Mcp

This project is the HTTP MCP adapter that lets an approved Samwise agent inspect Samwise and tune its steering configuration. It is hosted by `Samwise.Web` at `/mcp`; there is no additional process, database, container, or published port.

## Tools

Read tools use `IReadOnlyRepository<T>` and public specifications:

- `list_work_items` and `get_work_item`
- `get_agent_invocation` returns the exact recorded prompt, raw response, error, model settings, and available-tool snapshot for one agent run.
- `list_input_events` and `get_input_event`
- `list_playbooks`
- `get_agent_settings`, including each stage's allowed placeholder legend

Mutation tools send externally triggered application use cases through MediatR:

- `save_agent_prompt` replaces one complete stage prompt while preserving its model and reasoning effort.
- `save_playbook` creates or updates a Playbook, including its enabled state.
- `replan_work_item` invalidates an unexecuted current plan and immediately generates its replacement while retaining plan history.
- `restore_work_item` restores a discarded Work Item into a new planning revision while retaining its prior plans and discard metadata.

The adapter does not expose endpoint credentials, owner identity data, email-account passwords, direct repository mutation, Work Item approval, or Work Item execution. Replanning and explicit restoration are the only Work Item lifecycle mutations it exposes.

## Structure

- `Tools/` contains one MCP tool group per Samwise resource.
- `Serialization/` owns the consistent JSON returned as tool content.
- `Authentication/` contains the dedicated API-key scheme.
- `Configuration/` contains the `SamwiseMcp` option model.
- `Installers/McpInstaller` registers Streamable HTTP transport and tools.
- `Endpoints/SamwiseMcpEndpointExtensions` maps the authenticated `/mcp` endpoint.

## Security flow

Every MCP request must carry `X-Api-Key` matching `SamwiseMcp:ApiKey`. In Compose, `SAMWISE_MCP_API_KEY` is available to both containers; configure Gateway's environment-header mapping as `X-Api-Key=SAMWISE_MCP_API_KEY`. Gateway reaches the service as `http://web:8080/mcp` on `gateway-backend`; no additional host port is published for MCP. Gateway project grants remain the fine-grained boundary: give planning the read tools and give execution the two write tools only when self-tuning is intended and approved.

Prompt and Playbook writes use the same validation and persistence flow as the UI. Tool responses contain validation errors instead of bypassing domain/application rules.
