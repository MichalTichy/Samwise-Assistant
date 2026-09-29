# Samwise Assistant

Samwise is a single-user personal assistant that turns incoming information into approved, executable work.

Version 1 accepts manual text, received and sent email messages, configured Microsoft Teams activity, and Todoist tasks that are due soon in the password-protected Blazor UI. Inputs do **not** create work items directly. They enter the same canonical inbound-event pipeline:

```mermaid
flowchart LR
    Capture["Capture inbound event"] --> Intake["AI intake classification"]
    Intake -->|create_work_item| Create["Create work item"]
    Intake -->|append_to_work_item| Append["Append context and invalidate plan"]
    Intake -->|ignore| Ignore["Record ignored"]
    Intake -->|needs_review| Review["Record needs review"]
    Create --> Plan["Generate technical plan"]
    Append --> Plan
    Plan -->|plan_required| Approval["Wait for user approval"]
    Plan -->|already_resolved| Done["Complete work item"]
    Plan -->|no_longer_relevant| Discard["Discard work item"]
    Approval -->|approve| Execute["Execute with execution Gateway key"]
    Approval -->|reject + feedback| Replan["Increment revision and replan"]
    Approval -->|discard| Discard["Discard work item"]
    Replan --> Plan
    Execute -->|confirmed success| Done["Complete work item"]
    Execute -->|interrupted / uncertain| Unknown["Outcome unknown; reconcile manually"]
```

The authoritative state lives in PostgreSQL. AI calls are one-shot and stateless; Samwise stores the exact rendered prompt, structured result, selected playbook snapshot, tool-catalog versions, work item revision, and approved plan needed for audit and continuation. An interrupted execution is never retried automatically because its external effects may already have happened.

## Technology

- .NET 10 and Blazor Interactive Server
- LumexUI with Tailwind CSS 4 for the web interface
- ASP.NET Core Identity password hashing and cookie authentication for one owner account
- PostgreSQL with Marten for Samwise documents
- MailKit for read-only IMAP ingestion with password or app-password authentication
- one versioned work item aggregate containing its plans, plan reviews, comments, discard metadata, and exact tool-catalog snapshots
- PostgreSQL with EF Core migrations for Identity
- .NET Aspire for local orchestration
- Microsoft Agent Framework `ChatClientAgent`
- Official OpenAI .NET connector pointed at Codex CLI API Gateway
- Official MCP C# SDK for Samwise's authenticated self-inspection and self-tuning endpoint

## Projects

- [Samwise.Domain](src/Samwise.Domain/README.md) — work item, plan, playbook, agent-stage configuration, inbound-event, comment, and invocation state
- [Samwise.Logic](src/Samwise.Logic/README.md) — externally triggered MediatR use cases, reusable workflow services, public read specifications, playbook steering, prompt rendering, and UI-facing models
- [Samwise.Inputs.Manual](src/Samwise.Inputs.Manual/README.md) — manual-input event and submit use case
- [Samwise.Inputs.Email](src/Samwise.Inputs.Email/README.md) — email documents, events, ports, processing, configuration, and initialization
- [Samwise.Inputs.Teams](src/Samwise.Inputs.Teams/README.md) — Teams documents, events, ports, processing, configuration, and initialization
- [Samwise.Inputs.Todoist](src/Samwise.Inputs.Todoist/README.md) — Todoist task events, due-window processing, configuration, and initialization
- [Samwise.Infrastructure.Persistence.Marten](src/Samwise.Infrastructure.Persistence.Marten/README.md) — only Marten registration and Samwise document mappings
- [Samwise.Infrastructure.AgentRuntimes](src/Samwise.Infrastructure.AgentRuntimes/README.md) — provider-neutral endpoint configuration plus the standard OpenAI/Agent Framework implementation of `IAgentRuntime`
- [Samwise.Infrastructure.CodexGateway](src/Samwise.Infrastructure.CodexGateway/README.md) — only the Gateway-specific model-capability and versioned MCP tool catalogs
- [Samwise.Infrastructure.Email.Imap](src/Samwise.Infrastructure.Email.Imap/README.md) — provider-neutral read-only MailKit transport, password authentication, credential protection, and polling trigger
- [Samwise.Infrastructure.Email.Microsoft365](src/Samwise.Infrastructure.Email.Microsoft365/README.md) — Microsoft device-code authorization, protected MSAL sessions, token refresh, and XOAUTH2 authentication
- [Samwise.Infrastructure.Teams](src/Samwise.Infrastructure.Teams/README.md) — direct Microsoft Graph adapter for Teams sign-in, catalog lookup, message polling, and transcript discovery
- [Samwise.Infrastructure.Todoist](src/Samwise.Infrastructure.Todoist/README.md) — read-only Todoist API v1 adapter and task polling
- [Samwise.Infrastructure.WebPush](src/Samwise.Infrastructure.WebPush/README.md) — VAPID keys and Web Push delivery for work-item notifications
- [Samwise.Infrastructure.Mcp](src/Samwise.Infrastructure.Mcp/README.md) — authenticated first-party MCP read tools plus prompt and Playbook commands
- [Samwise.Infrastructure.Persistence.Identity](src/Samwise.Infrastructure.Persistence.Identity/README.md) — ASP.NET Core Identity storage, migration, and owner initializer
- [Samwise.Web](src/Samwise.Web/README.md) — authenticated Blazor composition root and presentation
- [Samwise.AppHost](src/Samwise.AppHost/README.md) — local PostgreSQL and web orchestration
- [Samwise.ServiceDefaults](src/Samwise.ServiceDefaults/README.md) — health checks, telemetry, and service discovery defaults
- [Samwise.Tests](tests/Samwise.Tests/README.md) — domain, prompt, runtime, architecture, Gateway, and persistence tests
- `SharedInfrastructure/*` — repository-local copies of the installer, initializer, leader-election, specification, unit-of-work, and Marten building blocks used by the solution

Every project directory has its own README describing its internal structure, main components, call flow, and architectural boundary.

See [architecture.md](docs/architecture.md) for the detailed design and [gateway-agent-framework-compatibility.md](docs/gateway-agent-framework-compatibility.md) for the Gateway contract.

## Run locally

Prerequisites: .NET SDK 10.0.301 or compatible, Node.js, and Docker Desktop.

The generated stylesheet is committed for ordinary local builds. After changing
`src/Samwise.Web/Styles/app.css` or component markup, rebuild it with:

```powershell
cd src/Samwise.Web
npm ci
npm run build:css
```

Release builds regenerate the stylesheet automatically.

Before first launch, set `OwnerAccount:Email` and `OwnerAccount:InitialPassword` in
`src/Samwise.Web/appsettings.json`. The repository values are local first-run defaults and should
be changed before exposing the application beyond your machine.

Start the Aspire host:

```powershell
dotnet run --project src/Samwise.AppHost
```

Startup creates the first and only owner with those values. Sign in with the initial password, then
use **Change password** in the account menu. Later startups never reset an existing password from
configuration. Samwise has no registration, email confirmation, password recovery, external login,
2FA, or passkey UI.

Configure the Gateway's three profiles through user secrets or environment variables before exercising the AI workflow. Intake and planning may share the planning key; execution must use its separate key.

In the Gateway admin UI, create `samwise-planning` and `samwise-execution` projects and grant the
corresponding keys. The planning project should have no MCP tools, or only read-only tools that are
safe during planning. The execution project owns the exact approved execution allowlist. Samwise
retrieves that execution catalog separately and gives its metadata to the planner.

Samwise also serves its own authenticated MCP endpoint at `/mcp`. Configure it in Gateway as
`http://web:8080/mcp` with the environment-header mapping `X-Api-Key=SAMWISE_MCP_API_KEY`. Grant its read tools
to planning so the agent can inspect Work Items, agent invocations, input events, Playbooks, and agent settings. Grant
`save_agent_prompt`, `save_playbook`, and `replan_work_item` only to execution so mutations remain part of an
approved plan.

The Compose stack also runs [Mail, Calendar & Contacts MCP](https://github.com/MichalTichy/MCP.MailCalendarContacts),
[Toggl MCP](https://github.com/MichalTichy/MCP.TogglTrack), and the official [Todoist MCP](https://github.com/Doist/todoist-mcp) server on the private
`codex-gateway-backend` network. Their tools remain behind the Gateway: only tools explicitly
enabled on each trusted server and granted to `samwise-execution` can be planned and invoked.
Version 1 must leave `send_email` and `unsubscribe_from_email` ungranted.

```powershell
dotnet user-secrets set "AgentEndpoints:Intake:ApiKey" "..." --project src/Samwise.Web
dotnet user-secrets set "AgentEndpoints:Planning:ApiKey" "..." --project src/Samwise.Web
dotnet user-secrets set "AgentEndpoints:Execution:ApiKey" "..." --project src/Samwise.Web
dotnet user-secrets set "SamwiseMcp:ApiKey" "..." --project src/Samwise.Web
dotnet user-secrets set "ManualInputApi:ApiKey" "..." --project src/Samwise.Web
```

Mobile automations can submit directly to the normal intake queue without opening a confirmation page:

```http
POST /api/manual-input?type=shared-link
X-Samwise-Input-Key: <manual-input-api-key>
Content-Type: application/json

{
  "text": "Required input text",
  "title": "Optional title",
  "url": "https://example.com",
  "note": "Optional additional context"
}
```

Only `text` is required. `type`, `title`, `url`, and `note` are optional context for intake classification. A successful request returns `202 Accepted` immediately; processing continues through the same background intake queue as the web form and other inputs. Use the dedicated manual-input key only in the `X-Samwise-Input-Key` header, never in a query string.

Base addresses have local defaults in `src/Samwise.Web/appsettings.json`; override them when the Gateway is hosted elsewhere. After signing in, configure the model, reasoning effort, and prompt for Intake, Planning, and Execution on **Agent settings**. Add optional enabled Playbooks to help intake recognize actionable scenarios and steer the planning agent.

Configure one or more IMAP accounts under **Configuration → Inputs → Emails**. Generic providers may use a password/app password. Exchange Online requires Microsoft 365 OAuth: configure `SAMWISE_MICROSOFT365_CLIENT_ID` and `SAMWISE_MICROSOFT365_TENANT_ID`, select **Microsoft 365 sign-in**, and complete the displayed device-code login. Account creation records the current INBOX and Sent UIDs as starting checkpoints, so historical mail is not imported automatically. Samwise then polls enabled accounts at the UI-configured interval and captures every new received or sent message without changing message flags. A sent message may update an active Work Item and trigger replanning; planning can then keep work pending, complete an objective proven resolved, or discard one proven irrelevant. The manual action continues to search unread INBOX mail regardless of age. Passwords and Microsoft token caches are encrypted with the persisted Data Protection key ring.

## Verify

```powershell
dotnet restore Samwise.slnx
dotnet build Samwise.slnx --no-restore
dotnet test tests/Samwise.Tests/Samwise.Tests.csproj --no-build --no-restore
```

## Deploy with Docker Compose

The root `Dockerfile` builds the single `Samwise.Web` runtime image. The root `compose.yaml` runs that image together with Codex CLI API Gateway, its isolated runner, Mail, Calendar & Contacts MCP, Teams MCP, Todoist MCP, Riganti IS MCP, and Toggl MCP, using an existing PostgreSQL instance. Samwise's Teams intake connects directly to Microsoft Graph and its Todoist intake connects directly to the Todoist API; the separate Teams MCP is only an optional Gateway tool provider for agents. Persistent volumes retain Gateway state, Codex authentication, MCP configuration, and ASP.NET Core Data Protection keys.

Copy `.env.example` to the ignored `.env`, replace every secret placeholder, then run:

```powershell
docker compose pull
docker compose up -d
```

See [deployment.md](docs/deployment.md) for reverse-proxy, persistence, update, build, and registry details.

No database password or Gateway API key is checked into this repository. The checked-in owner
password is an intentionally disposable local first-run default.
