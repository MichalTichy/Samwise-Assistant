# Container deployment

`compose.yaml` runs Samwise together with Codex CLI API Gateway, the Gateway's isolated Codex runner image, Mail, Calendar & Contacts MCP, the official Todoist MCP server, and Teams MCP. It connects Samwise and Gateway to an existing PostgreSQL instance; it does not start or manage PostgreSQL.

## First deployment

1. Copy `.env.example` to `.env` beside `compose.yaml`.
2. Create separate `samwise` and `codex_gateway` databases on the existing PostgreSQL instance, then configure both connection strings. The applications create their own schemas and tables. When PostgreSQL runs on the Docker host, use `host.docker.internal` rather than `localhost`.
3. Replace the owner credentials, Gateway admin password, MCP admin credentials, `TODOIST_API_KEY`, and Microsoft Entra settings for Teams MCP. Placeholder Samwise project keys are sufficient for the first Gateway startup. Each Gateway API URL must be absolute and end in `/v1/`.
4. Pull and start the stack:

   ```bash
   docker compose pull
   docker compose up -d
   ```

5. Open `http://<host>:5051/admin/ui`, sign in with `MAIL_CALENDAR_CONTACTS_MCP_ADMIN_TOKEN`, and configure the provider accounts. Replace `MAIL_CALENDAR_CONTACTS_MCP_EXTERNAL_URL` with this reachable URL before using a provider that needs an OAuth callback.
6. Open `http://<host>:5052/admin`, sign in with `TEAMS_MCP_ADMIN_PASSWORD`, and connect the delegated Microsoft account.
7. Open `http://<host>:5050/admin` from the local network, sign in to the Gateway, complete Codex device authentication, and create the `samwise-planning` and `samwise-execution` projects and their keys. `CODEX_GATEWAY_BIND_ADDRESS=0.0.0.0` exposes the configured Gateway port on every IPv4 interface; change it to a specific host address if desired.
8. Put the real intake, planning, and execution keys in `.env`, then recreate Samwise with `docker compose up -d web`.
9. Open Samwise on its configured host and port, sign in, and change the initial password.
10. Remove `SAMWISE_OWNER_INITIAL_PASSWORD` from `.env` after the owner has a password. An empty value is valid on subsequent starts.

Samwise listens on plain HTTP port `8080` inside its container and the Gateway listens on `8080` inside its container. Their default published host ports are `8080` and `5050`. The Compose default `SAMWISE_REQUIRE_HTTPS=false` allows login when Samwise is opened directly over HTTP on a trusted network. For any internet-accessible deployment, terminate HTTPS in the host reverse proxy and set `SAMWISE_REQUIRE_HTTPS=true`; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` lets Samwise honor the proxy's forwarded scheme.

The Gateway control plane and Mail/Calendar/Contacts MCP container intentionally do not use Compose's `no-new-privileges` option. Some Linux hosts refuse to start .NET processes under that option. The MCP containers run as dedicated non-root users and remain isolated on the private backend network, while dynamically created runner containers retain the Gateway's stricter runtime security restrictions. Samwise waits for the Gateway `/health` endpoint before it starts, so a failed Gateway keeps the stack visibly unhealthy instead of surfacing later as a transient catalog error.

## Mail, calendar, and contacts MCP

The [MCP.MailCalendarContacts](https://github.com/MichalTichy/MCP.MailCalendarContacts) server is included in the Samwise stack. Samwise never connects to it directly. The private-network deployment does not require an MCP transport API key; Gateway enforces the exact tool grant associated with each Samwise API key through its run-scoped relay. The admin UI remains protected by `MAIL_CALENDAR_CONTACTS_MCP_ADMIN_TOKEN` and is published on port `5051` for the trusted LAN.

For Portainer:

1. Deploy or update the single Samwise stack. The checked-in `.env.example` pins the reviewed Gateway, runner, and MCP images; copy its variables into the Portainer stack environment and replace every secret placeholder.
2. Open the Gateway admin UI and add an HTTP MCP server with these values:

   | Field | Value |
   | --- | --- |
   | Name | `Mail, Calendar & Contacts` |
   | URL | `http://mail-calendar-contacts-mcp:8080` |
   | Execution mode | `Gateway` |

3. Test the connection and discover the server's tools. Enable only the tools that the Gateway may expose.
4. Grant read tools to intake/planning only when needed, and grant the desired mutation subset to `samwise-execution`. Calendar/contact writes and mail-management tools can be enabled individually. `reply_email`, `send_email`, and `unsubscribe_from_email` are execution-only capabilities.

The intake and planning keys do not need permission to invoke these MCP tools. Samwise reads the execution key's Gateway tool catalog and supplies that catalog to the planning agent, so plans are constrained to capabilities the execution stage will actually receive.

## Todoist MCP

The stack runs the official [`@doist/todoist-mcp`](https://github.com/Doist/todoist-mcp) HTTP server from its pinned npm release. It has no published host port: only Gateway can reach it on the private backend network. `TODOIST_API_KEY` is passed directly to that container and is not copied into Gateway or Samwise. The container downloads the pinned package on its first start, so it needs outbound access to the npm registry when it is created.

Create a Todoist API token in **Todoist → Settings → Integrations → Developer**, put it in the Portainer stack environment as `TODOIST_API_KEY`, and add this HTTP MCP server in the Gateway admin UI:

| Field | Value |
| --- | --- |
| Name | `Todoist` |
| URL | `http://todoist-mcp:8080/mcp` |
| Execution mode | `Gateway` |

Do not configure an HTTP header for this server. The API token authenticates the MCP server to Todoist; network access and Gateway project grants protect MCP invocation. Test discovery, enable the desired tools, and grant them to `samwise-execution`. Grant only read tools if the agent should inspect Todoist without changing it; task creation, update, completion, and deletion tools are separate execution capabilities.

## Teams MCP

The stack runs [`MCP.Teams`](https://github.com/MichalTichy/MCP.Teams) as `registry.tichymichal.net/mcp-teams`. Its MCP endpoint stays on the Gateway backend network, while port `5052` exposes its password-protected account page to the trusted LAN. Configure a Microsoft Entra app as described in that repository, then connect the single delegated Microsoft account at `http://<host>:5052/admin`.

Add it to Gateway as an HTTP MCP server named `Microsoft Teams` with URL `http://teams-mcp:8080`, Gateway execution mode, and no headers. Read tools can be granted to intake/planning. Sending chat/channel messages and creating, updating, or cancelling Teams meetings must be granted only to execution. Call-history tools require the Entra application's `CallRecords.Read.All` application permission and client secret.

## Persistent state

The Compose stack creates five named volumes:

- `data-protection-keys` keeps login cookies valid across container replacement.
- `mcp-mail-calendar-contacts-data` stores MCP provider configuration, tokens, and local state.
- `mcp-teams-data` stores the delegated Microsoft token cache.
- `codex-gateway-data` is the workspace volume mounted into isolated Gateway runner containers.
- `codex-gateway-auth` stores the Codex authentication used by the Gateway.

Back up these volumes and the two PostgreSQL databases. Losing only the data-protection volume signs the owner out but does not remove work items or credentials.

## Updating

The Compose defaults use the `latest` Samwise, Gateway, runner, Mail/Calendar/Contacts MCP, and Teams MCP images plus the current Node 24 Alpine runtime. The checked-in `.env.example` pins the currently verified Gateway pair (`decfeca`), Mail/Calendar/Contacts MCP (`0903bf4`), Teams MCP (`7292ca0`), and Node runtime digest. The official Todoist package is pinned directly in `compose.yaml`. Prefer immutable tags and digests in the deployed `.env`, then run:

```bash
docker compose pull
docker compose up -d
```

## Building and publishing

Build from the repository root. Replace `<commit>` with the Git commit being published:

```bash
docker build --tag registry.tichymichal.net/samwise-assistant:<commit> --tag registry.tichymichal.net/samwise-assistant:latest .
docker push registry.tichymichal.net/samwise-assistant:<commit>
docker push registry.tichymichal.net/samwise-assistant:latest
```

The Gateway repository builds two targets. Publish its `gateway` target as `registry.tichymichal.net/codex-gateway:<commit>` and its `runner` target as `registry.tichymichal.net/codex-gateway-runner:<commit>`, plus the corresponding `latest` tags. The one-shot `gateway-runner-image` Compose service ensures Docker pulls the exact runner image before the Gateway starts.

```bash
docker build --target gateway --tag registry.tichymichal.net/codex-gateway:<commit> --tag registry.tichymichal.net/codex-gateway:latest .
docker build --target runner --tag registry.tichymichal.net/codex-gateway-runner:<commit> --tag registry.tichymichal.net/codex-gateway-runner:latest .
docker push registry.tichymichal.net/codex-gateway:<commit>
docker push registry.tichymichal.net/codex-gateway:latest
docker push registry.tichymichal.net/codex-gateway-runner:<commit>
docker push registry.tichymichal.net/codex-gateway-runner:latest
```
