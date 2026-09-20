# Container deployment

`compose.yaml` runs Samwise together with Codex CLI API Gateway, the Gateway's isolated Codex runner image, Mail, Calendar & Contacts MCP, Teams MCP, and the official Todoist MCP server. It connects Samwise and Gateway to an existing PostgreSQL instance; it does not start or manage PostgreSQL. Samwise itself does not depend on Teams MCP for intake: it connects directly to Microsoft Graph.

## First deployment

1. Copy `.env.example` to `.env` beside `compose.yaml`.
2. Create separate `samwise` and `codex_gateway` databases on the existing PostgreSQL instance, then configure both connection strings. The applications create their own schemas and tables. When PostgreSQL runs on the Docker host, use `host.docker.internal` rather than `localhost`.
3. Replace the owner credentials, Gateway admin password, `SAMWISE_MCP_API_KEY`, Mail/Calendar/Contacts MCP admin token, and `TODOIST_API_KEY`. To ingest Exchange Online mail, also configure the Microsoft 365 public-client application described below. Placeholder Samwise project keys are sufficient for the first Gateway startup. Each Gateway API URL must be absolute and end in `/v1/`.
4. Pull and start the stack:

   ```bash
   docker compose pull
   docker compose up -d
   ```

5. Open `http://<host>:5051/admin/ui`, sign in with `MAIL_CALENDAR_CONTACTS_MCP_ADMIN_TOKEN`, and configure the provider accounts. Replace `MAIL_CALENDAR_CONTACTS_MCP_EXTERNAL_URL` with this reachable URL before using a provider that needs an OAuth callback.
6. Open `http://<host>:5050/admin` from the local network, sign in to the Gateway, complete Codex device authentication, and create the `samwise-planning` and `samwise-execution` projects and their keys. `CODEX_GATEWAY_BIND_ADDRESS=0.0.0.0` exposes the configured Gateway port on every IPv4 interface; change it to a specific host address if desired.
7. Put the real intake, planning, and execution keys in `.env`, then recreate Samwise with `docker compose up -d web`.
8. Open Samwise on its configured host and port, sign in, and change the initial password.
9. Remove `SAMWISE_OWNER_INITIAL_PASSWORD` from `.env` after the owner has a password. An empty value is valid on subsequent starts.

Samwise listens on plain HTTP port `8080` inside its container and the Gateway listens on `8080` inside its container. Their default published host ports are `8080` and `5050`. The Compose default `SAMWISE_REQUIRE_HTTPS=false` allows login when Samwise is opened directly over HTTP on a trusted network. For any internet-accessible deployment, terminate HTTPS in the host reverse proxy and set `SAMWISE_REQUIRE_HTTPS=true`; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` lets Samwise honor the proxy's forwarded scheme.

The Gateway control plane and Mail/Calendar/Contacts MCP container intentionally do not use Compose's `no-new-privileges` option. Some Linux hosts refuse to start .NET processes under that option. The MCP containers run as dedicated non-root users and remain isolated on the private backend network, while dynamically created runner containers retain the Gateway's stricter runtime security restrictions. Samwise waits for the Gateway `/health` endpoint before it starts, so a failed Gateway keeps the stack visibly unhealthy instead of surfacing later as a transient catalog error.

## Microsoft 365 email ingestion

Exchange Online does not accept IMAP username/password or app-password authentication. Create a public-client application in Microsoft Entra ID for Samwise:

1. Add the delegated **Office 365 Exchange Online → IMAP.AccessAsUser.All** API permission and grant any consent required by the organization.
2. Under **Authentication → Advanced settings**, enable **Allow public client flows**. No client secret or callback URL is needed for device-code sign-in.
3. Set `SAMWISE_MICROSOFT365_CLIENT_ID` to the application's client ID. Set `SAMWISE_MICROSOFT365_TENANT_ID` to the organization's tenant ID for a single-tenant registration, or keep `organizations` for a compatible multi-tenant registration.
4. Recreate the `web` container. In **Configuration → Inputs → Emails**, select **Microsoft 365 sign-in**, open the displayed Microsoft URL, enter the code, and then save the account.

Samwise requests delegated IMAP access only. Its refreshable MSAL token cache is encrypted in the `EmailAccount` document using the persisted Data Protection key ring.

## Microsoft Teams ingestion

Samwise reads Teams intake directly from Microsoft Graph; the Teams MCP server is not part of this intake path. Create a public-client Entra application and enable **Allow public client flows**. Grant the delegated permissions listed for Teams communication: `User.Read`, `Chat.ReadWrite`, `ChatMessage.Send`, `Team.ReadBasic.All`, `Channel.ReadBasic.All`, `ChannelMessage.Read.All`, `ChannelMessage.Send`, and `Calendars.ReadWrite`. Enter the tenant ID and application client ID, then connect the account under **Configuration → Inputs → Microsoft Teams**. No Teams intake credentials are required in the container environment.

Transcript discovery is optional. When it is enabled, also grant `CallRecords.Read.All`, `OnlineMeetingTranscript.Read.All`, and `CallTranscripts.Read.All` as application permissions, grant admin consent, create a client secret, and enter that secret in the same Teams configuration page. Samwise protects it with ASP.NET Core Data Protection before persistence and stores the delegated MSAL cache in its existing persisted data-protection volume. The standalone Teams MCP may still be registered with Gateway when planning or execution agents need Teams tools; it is an independent integration with its own connection and credentials.

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
4. Grant the desired subset to the `samwise-execution` project/key. Start with mail, calendar, and contact read tools. Calendar/contact write and mail-management tools can be enabled individually when wanted. Do not grant `send_email` or `unsubscribe_from_email` in version 1.

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

## Samwise self-management MCP

Samwise hosts its own MCP endpoint in the web container. Put a long random value in `SAMWISE_MCP_API_KEY`, then add this HTTP MCP server in the Gateway admin UI:

| Field | Value |
| --- | --- |
| Name | `Samwise` |
| URL | `http://web:8080/mcp` |
| Execution mode | `Gateway` |
| Environment headers | `X-Api-Key=SAMWISE_MCP_API_KEY` |

Gateway's environment-header field expects the environment-variable name, not the secret itself. Compose supplies `SAMWISE_MCP_API_KEY` to both Gateway and Samwise; Gateway resolves it only when connecting to the MCP server. The web container joins `gateway-backend`, allowing Gateway metadata discovery and invocation without another container or host port. The normal Samwise web port still serves `/mcp`, but requests without the separate API key receive `401 Unauthorized`.

Enable `list_work_items`, `get_work_item`, `get_agent_invocation`, `list_input_events`, `get_input_event`, `list_playbooks`, and `get_agent_settings` for the planning project. Enable `save_agent_prompt`, `save_playbook`, and `replan_work_item` only for the execution project. This lets planning inspect current state and propose changes while keeping every mutation behind Work Item approval.

## Persistent state

The Compose stack creates four named volumes:

- `data-protection-keys` keeps login cookies valid across container replacement.
- `mcp-mail-calendar-contacts-data` stores MCP provider configuration, tokens, and local state.
- `codex-gateway-data` is the workspace volume mounted into isolated Gateway runner containers.
- `codex-gateway-auth` stores the Codex authentication used by the Gateway.

Back up these volumes and the two PostgreSQL databases. Losing only the data-protection volume signs the owner out but does not remove work items or credentials.

## Updating

The Compose defaults use the `latest` Samwise, Gateway, runner, and Mail/Calendar/Contacts MCP images plus the current Node 24 Alpine runtime. The checked-in `.env.example` pins the currently verified Gateway pair (`decfeca`), native-app-host MCP (`0903bf4`), and Node runtime digest. The official Todoist package is pinned directly in `compose.yaml`. Prefer immutable tags and digests in the deployed `.env`, then run:

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
