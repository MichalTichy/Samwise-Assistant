# Container deployment

`compose.yaml` runs Samwise, Gateway, a runner-image pull helper, and standard filesystem, memory, and Playwright tools. It uses existing PostgreSQL databases. Email and calendar integrations are commented examples enabled separately.

For an assistant-guided installation or configuration of existing deployments, start with
[AI-assisted setup](ai-setup.md). It includes optional local PostgreSQL and source-build
Compose overrides, temporary management MCP access, and end-to-end verification.

## First deployment

1. Copy `.env.example` to `.env` beside `compose.yaml`.
2. Create separate `samwise` and `codex_gateway` databases on the existing PostgreSQL instance, then configure both connection strings. The applications create their own schemas and tables. When PostgreSQL runs on the Docker host, use `host.docker.internal` rather than `localhost`.
3. Create `.data/mcp-files` beside `compose.yaml` for shared files. Generate a random `SAMWISE_OWNER_REGISTRATION_TOKEN` of at least 32 characters (or use `python scripts/ai_setup.py prepare`), leaving owner email/password blank. Replace the Gateway admin password, `SAMWISE_MCP_API_KEY`, and `SAMWISE_MANUAL_INPUT_API_KEY`. To ingest Exchange Online mail, also configure the Microsoft 365 public-client application described below. Placeholder Samwise project keys are sufficient for the first Gateway startup. Each Gateway API URL must be absolute and end in `/v1/`.
4. Pull and start the stack:

   ```bash
   docker compose pull
   docker compose up -d
   ```

5. Open `http://<host>:5050/admin` from the local network, sign in to the Gateway, complete Codex device authentication, and create the `samwise-planning` and `samwise-execution` projects and their keys. `CODEX_GATEWAY_BIND_ADDRESS=0.0.0.0` exposes the configured Gateway port on every IPv4 interface; change it to a specific host address if desired.
6. Put the real intake, planning, and execution keys in `.env`, then recreate Samwise with `docker compose up -d web`.
7. Generate the private installation link with `python scripts/ai_setup.py --samwise https://<samwise-host> registration-link`, then open the saved link and create the owner account with an email and a password. Local installs can use `http://localhost:<port>`. The account is independent of connected mailboxes.
8. Clear `SAMWISE_OWNER_REGISTRATION_TOKEN` in deployed configuration and remove the temporary link file. Registration is already disabled by the presence of the owner in the database; it stays disabled after restarts. Existing deployments keep their owner and sign in as before. Legacy email/initial-password provisioning remains supported when explicitly configured.

Samwise listens on plain HTTP port `8080` inside its container and the Gateway listens on `8080` inside its container. Their default published host ports are `8080` and `5050`. The Compose default `SAMWISE_REQUIRE_HTTPS=false` allows login when Samwise is opened directly over HTTP on a trusted network. For any internet-accessible deployment, terminate HTTPS in the host reverse proxy and set `SAMWISE_REQUIRE_HTTPS=true`; `ASPNETCORE_FORWARDEDHEADERS_ENABLED` lets Samwise honor the proxy's forwarded scheme.

Samwise waits for Gateway's health endpoint before starting. The standard tool servers run independently; register them in Gateway and grant their tools as described below.

## Manual input API

Set `SAMWISE_MANUAL_INPUT_API_KEY` to a long random value distinct from every Gateway and MCP key. A mobile Shortcut can submit directly to `POST /api/manual-input?type=<optional-hint>` with that value in the `X-Samwise-Input-Key` header. The JSON body requires only `text`; `title`, `url`, and `note` are optional. The endpoint is write-only, returns `202 Accepted`, and queues the event without waiting for intake processing or opening a confirmation page.

## Microsoft 365 email ingestion

Exchange Online does not accept IMAP username/password or app-password authentication. Create a public-client application in Microsoft Entra ID for Samwise:

1. Add the delegated **Office 365 Exchange Online → IMAP.AccessAsUser.All** API permission and grant any consent required by the organization.
2. Under **Authentication → Advanced settings**, enable **Allow public client flows**. No client secret or callback URL is needed for device-code sign-in.
3. Set `SAMWISE_MICROSOFT365_CLIENT_ID` to the application's client ID. Set `SAMWISE_MICROSOFT365_TENANT_ID` to the organization's tenant ID for a single-tenant registration, or keep `organizations` for a compatible multi-tenant registration.
4. Recreate the `web` container. In **Configuration → Inputs → Emails**, select **Microsoft 365 sign-in**, open the displayed Microsoft URL, enter the code, and then save the account.

Samwise requests delegated IMAP access only. Its refreshable MSAL token cache is encrypted in the `EmailAccount` document using the persisted Data Protection key ring.

## Microsoft Teams ingestion

Samwise reads Teams intake directly from Microsoft Graph; the Teams MCP server is not part of this intake path. Create a public-client Entra application and enable **Allow public client flows**. Grant only the delegated read permissions needed by intake: `User.Read`, `Chat.Read`, `Team.ReadBasic.All`, `Channel.ReadBasic.All`, and `ChannelMessage.Read.All`. Enter the tenant ID and application client ID, then connect the account under **Configuration → Inputs → Microsoft Teams**. No Teams intake credentials are required in the container environment. Agent writes remain the responsibility of the separately permissioned Teams MCP.

Transcript discovery is optional. When it is enabled, also grant `CallRecords.Read.All`, `OnlineMeetingTranscript.Read.All`, and `CallTranscripts.Read.All` as application permissions, grant admin consent, create a client secret, and enter that secret in the same Teams configuration page. Samwise protects it with ASP.NET Core Data Protection before persistence and stores the delegated MSAL cache in its existing persisted data-protection volume. The standalone Teams MCP may still be registered with Gateway when planning or execution agents need Teams tools; it is an independent integration with its own connection and credentials.

## Standard tools

These services start automatically without external accounts. Create `.data/mcp-files` beside `compose.yaml` and add files you want to share. Register each server in Gateway using execution mode **Gateway**, then discover and grant selected tools:

| Service | Gateway URL | Storage |
| --- | --- | --- |
| Filesystem | `http://filesystem-mcp:8000/mcp` | `.data/mcp-files`, mounted read-only at `/data` |
| Memory | `http://memory-mcp:8000/mcp` | `memory-data` volume, at `/data/memory.jsonl` |
| Playwright | `http://playwright-mcp:8931/mcp` | Isolated browser sessions; logins are discarded on session close |

[Filesystem](https://github.com/modelcontextprotocol/servers/tree/main/src/filesystem) and [memory](https://github.com/modelcontextprotocol/servers/tree/main/src/memory) use [Supergateway](https://github.com/supercorp-ai/supergateway) to bridge stdio to HTTP. Their npm packages download at startup. [Playwright](https://github.com/microsoft/playwright-mcp) runs headless Chromium from its public image.

These services publish no host ports. Gateway reaches them on `gateway-backend`. Grant memory mutations and browser actions that change external state only to execution behind plan approval. Filesystem write tools cannot modify the read-only mount. Deliberately remove `:ro` and grant writes if needed. Files in tool containers are not automatically shared with runners.

## Companion Microsoft and multi-provider MCP servers

For agent lookups and actions, separately deploy [MCP.MailCalendarContacts](https://github.com/MichalTichy/MCP.MailCalendarContacts) (mail, calendar and contacts) or [MCP.Teams](https://github.com/MichalTichy/MCP.Teams) (Teams context, meetings and transcripts). Follow their own authorization and deployment instructions; neither service is bundled here.

Join Gateway's backend network and register the HTTP MCP endpoints with execution mode **Gateway**. Their default network is `codex-gateway-backend`; setup with `--deployment-id` selects a different name, so match the actual `CODEX_GATEWAY_BACKEND_NETWORK` in both stacks. Keep these trusted-network MCP endpoints private. Grant selected read tools to planning and selected action tools to execution. For draft-only workflows, grant draft creation without send/reply/unsubscribe tools and review the provider account's separate capabilities.

These tools do not replace Samwise's built-in read-only inputs. Input authorization, tool authorization and background polling are distinct. Teams exclusions are not an allowlist protecting all new unselected conversations; check incoming and lookup scope before enabling either path.

## Optional integrations

Uncomment a complete service block in `compose.yaml`, its matching Gateway environment mapping, and any volumes named in its comments. Copy the corresponding commented settings from `.env.example` to `.env`, fill them in, and run `docker compose up -d`.

| Integration | Gateway URL | Gateway environment headers |
| --- | --- | --- |
| IMAP/SMTP email | `http://email-mcp:8080/mcp` | `Authorization=EMAIL_MCP_AUTHORIZATION` |
| CalDAV calendar | `http://calendar-mcp:8080/mcp` | `Authorization=CALDAV_MCP_AUTHORIZATION` |

Register enabled servers in Gateway with execution mode **Gateway**. Header mappings use the environment-variable name rather than the token. Discover and grant selected tools; grant writes only to execution. Leave email sending and unsubscribe tools ungranted in version 1. These integrations connect to existing mail/calendar servers; they do not host those services or require a particular commercial provider.

### Email

The example uses open-source [email-mcp](https://github.com/codefuturist/email-mcp). Prepare `config.toml` with its upstream setup wizard and copy it into `.data/email-mcp/` beside `compose.yaml`. Use credentials accessible inside the container rather than host OS keychain references. Ensure the container user can read the configuration. This directory is ignored by Git. Set `EMAIL_MCP_HTTP_TOKEN` and uncomment both email state/cache volumes. Gateway receives only the transport token; mailbox credentials remain in the mounted config.

This tool server is separate from Samwise's read-only email input, configured under **Configuration → Inputs → Emails**, which needs no additional container.

### Calendar

The example uses [caldav-mcp](https://github.com/gelse/caldav-mcp) with an existing CalDAV service such as Nextcloud or Radicale. Set `CALDAV_URL`, `CALDAV_USERNAME`, `CALDAV_PASSWORD`, a separate `CALDAV_MCP_API_KEY`, and `CALDAV_TIMEZONE`. Provider credentials reach only the calendar container; Gateway receives its transport token. This does not create an automatic calendar input source in Samwise.

### Existing deployments

Move formerly bundled integration service definitions, volumes, and environment variables into your own Compose override or separate stack before updating. Preserve existing volume names to retain provider state. Separate services must share a network with Gateway or expose a reachable authenticated endpoint. Update Gateway registrations accordingly and retain existing volumes during migration.

### Todoist input

Samwise's optional Todoist input connects directly to its API. Paste its token under **Configuration → Inputs → Todoist**; no MCP service or Compose variable is needed. The persisted Data Protection key ring protects the stored token.

## iPhone notifications

Samwise sends Web Push notifications. The HTTPS requirement below applies to your iPhone only, because iOS accepts web notifications only from a site opened from the Home Screen over trusted HTTPS. The Samwise container itself needs outbound HTTPS to the push services, such as `*.push.apple.com`.

1. Set `SAMWISE_WEB_PUSH_SUBJECT` to a `mailto:` or `https:` contact. This is optional only when `SAMWISE_OWNER_EMAIL` is configured as its fallback. Web registration leaves that legacy variable blank, so set an explicit contact for such installations. Apple rejects pushes without a valid subject.
2. Open Samwise in Safari on the iPhone, tap **Share → Add to Home Screen**, and open Samwise from the new icon.
3. Sign in, open **More → Notifications**, tap **Enable notifications**, and allow them.
4. Tap **Send test notification**.

The VAPID keys are created on first start and stored in the database, with the private key protected by the Data Protection key ring. Keep the `data-protection-keys` volume: losing it makes the stored key unreadable, and every device has to enable notifications again.

## Samwise self-management MCP

Samwise hosts its own MCP endpoint in the web container. Put a long random value in `SAMWISE_MCP_API_KEY`, then add this HTTP MCP server in the Gateway admin UI:

| Field | Value |
| --- | --- |
| Name | `Samwise` |
| URL | `http://web:8080/mcp` |
| Execution mode | `Gateway` |
| Environment headers | `X-Api-Key=SAMWISE_MCP_API_KEY` |

Gateway's environment-header field expects the environment-variable name, not the secret itself. Compose supplies `SAMWISE_MCP_API_KEY` to both Gateway and Samwise; Gateway resolves it only when connecting to the MCP server. The web container joins `gateway-backend`, allowing Gateway metadata discovery and invocation without another container or host port. The normal Samwise web port still serves `/mcp`, but requests without the separate API key receive `401 Unauthorized`.

Enable `list_work_items`, `get_work_item`, `get_agent_invocation`, `list_input_events`, `get_input_event`, `list_playbooks`, and `get_agent_settings` for the planning project. Enable `save_agent_prompt`, `save_playbook`, `delete_playbook`, and `replan_work_item` only for the execution project. This lets planning inspect current state and propose changes while keeping every mutation behind Work Item approval.

## Optional automatic approval

Inspect the approval profile's actual `/tools` response when configuring it. Available
tools, including Gateway's native workspace shell, do not prevent automatic approval.
Samwise records that catalog in the invocation history; it does not attach tool definitions
to the approval inference request. The approval prompt asks for a decision and reason only.
Gateway-side capabilities remain governed by the selected profile.

Automatic approval is off for every existing and new playbook until **Suitable for automatic approval** is enabled on that playbook. Create two additional Gateway projects/API keys:

| Stage | Environment variables | Gateway grants |
| --- | --- | --- |
| Automatic approval | `SAMWISE_AUTO_APPROVAL_BASE_URL`, `SAMWISE_AUTO_APPROVAL_API_KEY` | Keep grants limited to the review role. An exposed tool does not itself block assessment. |
| Auto-approved execution | `SAMWISE_AUTO_EXECUTION_BASE_URL`, `SAMWISE_AUTO_EXECUTION_API_KEY` | Only the small set of tools permitted without human review. |

The default URLs point to `/p/samwise-auto-approval/v1/` and `/p/samwise-auto-execution/v1/`. Configure models, reasoning effort, and prompts for both new stages under **Agent settings**. Empty API keys and models are valid while automatic approval is unused; an eligible plan then stays available for manual review with a recorded reason.

After planning, Samwise considers only a plan with a selected eligible playbook. It checks that every proposed tool call exists with the same schema in the restricted execution catalog and every side-effecting step names a tool call. The approval profile catalog is recorded for audit; the presence of tools does not itself block assessment. The approval request asks for a decision and reason. A refusal or unavailable dependency leaves the plan awaiting normal user approval. An approval is bound to the current plan ID and Work Item revision, and execution uses the restricted Gateway profile. Retry of an uncertain automatic execution also uses that restricted profile.

## Persistent state

The Compose stack creates four named volumes:

- `data-protection-keys` keeps login cookies valid across container replacement.
- `memory-data` stores the MCP knowledge graph.
- `codex-gateway-data` is the workspace volume mounted into isolated Gateway runner containers.
- `codex-gateway-auth` stores the Codex authentication used by the Gateway.

Back up these volumes, `.data/mcp-files`, and both PostgreSQL databases. When email is enabled, also back up its configuration and state/cache volumes. Losing the Data Protection key ring invalidates cookies and makes protected credentials unreadable.

## Updating

Core images use `latest`; standard tools use Node 24 and the latest npm/Playwright releases. Optional integrations use mutable image tags too. Pin versions or digests for reproducibility. Explicit image variables override defaults. To pull and deploy updated images:

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
