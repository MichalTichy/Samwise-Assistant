# Container deployment

`compose.yaml` runs Samwise together with Codex CLI API Gateway, the Gateway's isolated Codex runner image, and Mail, Calendar & Contacts MCP. It connects Samwise and Gateway to an existing PostgreSQL instance; it does not start or manage PostgreSQL.

## First deployment

1. Copy `.env.example` to `.env` beside `compose.yaml`.
2. Create separate `samwise` and `codex_gateway` databases on the existing PostgreSQL instance, then configure both connection strings. The applications create their own schemas and tables. When PostgreSQL runs on the Docker host, use `host.docker.internal` rather than `localhost`.
3. Replace the owner credentials, Gateway admin password, and MCP admin token. Placeholder Samwise project keys are sufficient for the first Gateway startup. Each Gateway API URL must be absolute and end in `/v1/`.
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

The Gateway control plane and MCP container intentionally do not use Compose's `no-new-privileges` option. Some Linux hosts refuse to start .NET processes under that option. Both containers still run as dedicated non-root users; the MCP remains isolated on the private backend network, while dynamically created runner containers retain the Gateway's stricter runtime security restrictions. Samwise waits for the Gateway `/health` endpoint before it starts, so a failed Gateway keeps the stack visibly unhealthy instead of surfacing later as a transient catalog error.

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

## Persistent state

The Compose stack creates four named volumes:

- `data-protection-keys` keeps login cookies valid across container replacement.
- `mcp-mail-calendar-contacts-data` stores MCP provider configuration, tokens, and local state.
- `codex-gateway-data` is the workspace volume mounted into isolated Gateway runner containers.
- `codex-gateway-auth` stores the Codex authentication used by the Gateway.

Back up these volumes and the two PostgreSQL databases. Losing only the data-protection volume signs the owner out but does not remove work items or credentials.

## Updating

The Compose defaults use the `latest` Samwise, Gateway, runner, and MCP images. The checked-in `.env.example` pins the currently verified Gateway pair (`decfeca`) and native-app-host MCP (`0903bf4`). Prefer immutable tags in the deployed `.env`, then run:

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
