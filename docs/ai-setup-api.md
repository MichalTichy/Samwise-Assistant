# Setup management operations

Both applications expose temporary administration APIs only when `Setup__ApiKey` is configured. Each application has a different random key, supplied in `X-Setup-Key`. Blank configuration returns 404; missing/wrong credentials return 401. Ordinary owner cookies, inference credentials, and Samwise MCP keys do not authorize setup. Use HTTPS remotely or a localhost tunnel. Remove the deployed setup keys and restart when done. Do not grant setup management to Samwise runtime agents.

Use `scripts/ai_setup.py` or the temporary `scripts/setup_mcp.py` bridge so credentials are read from a private env file. Global URL/env options precede CLI subcommands. A body file may use `{"$env":"NAME"}` instead of a provider secret. Gateway header mappings instead contain the plain **variable name**, because Gateway resolves them in its own process.

## Samwise

Paths are relative to `/api/setup`. JSON property names use camelCase.

| Operation | Request/result |
| --- | --- |
| `GET /status` | Readiness booleans and configured stage models/efforts; no keys or URLs with credentials. |
| `GET /agents` | Full current prompts, stage models/efforts, allowed placeholders. |
| `GET /agents/{stage}/models` | Advertised model IDs, `supportedReasoningEfforts`, `defaultReasoningEffort`. |
| `PUT /agents/{stage}` | `{model,reasoningEffort,prompt?}`; omitted prompt is preserved. Returns `{succeeded,errors}`. Check `succeeded`, not merely HTTP 200. |
| `GET /playbooks` | Current playbooks including disabled entries and full guidance. |
| `POST /playbooks` | `{playbookId:null|existing-id,name,applicability,description,isEnabled,suitableForAutomaticApproval:false}` → `{succeeded,playbookId,errors}`. Omit/null ID only for a new playbook. |
| `GET /inputs` | Nonsecret email, Todoist, and Teams configuration and connection indicators. Preserve filters/lists on update. |
| `PUT /inputs/email` | `{pollingIntervalMinutes:5,ignoredSenderAddresses:[]}` |
| `POST /inputs/email/accounts` | Email account body below; reuse ID when updating. |
| `POST /inputs/email/authorize` | Start Microsoft email device authorization; see source use case for app/client settings and returned challenge. |
| `PUT /inputs/todoist` | Todoist body below; null token preserves existing credential. |
| `PUT /inputs/teams` | Teams body below; null secret preserves existing credential. |
| `POST /inputs/teams/authorize` | Start Teams device authorization after saving tenant/client settings. |
| `GET /inputs/teams/authorization` | Inspect current Teams connection/challenge state. |
| `GET /input-events?limit=10` | `{inputEvents:[...]}` with handling status, explanations and linked outcomes. |
| `GET /work-items?limit=10` | `{workItems:[...]}` with status, pending question and plans. Private evidence: inspect only what's relevant. |

Stages: `Intake`, `Planning`, `Execution`, `AutomaticApproval`, `AutoApprovedExecution`. Models and effort strings come from the relevant stage's catalog; do not assume a particular model exists.

Generic email account:

```json
{
  "emailAccountId": null,
  "displayName": "My mailbox",
  "emailAddress": "owner@example.com",
  "host": "imap.example.com",
  "port": 993,
  "security": 0,
  "authenticationMethod": 0,
  "username": "owner@example.com",
  "password": {"$env":"MY_MAIL_PASSWORD"},
  "microsoftAuthorizationId": null,
  "intakeGuidance": "This is my mailbox. Capture my remaining commitments and requests addressed to me; ignore routine receipts.",
  "isEnabled": true
}
```

Security `0` is SSL, `1` STARTTLS. Authentication `0` is password, `1` Microsoft 365 OAuth. For Microsoft 365 use the actual authorization ID from the owner-completed device flow instead of a password. Updating an existing account with null password preserves its stored credential. Account creation establishes current mailbox checkpoints, so it is not a historical import.

Todoist input:

```json
{
  "apiToken": {"$env":"MY_TODOIST_TOKEN"},
  "removeApiToken": false,
  "pollingIntervalMinutes": 15,
  "leadHours": 24,
  "overdueDays": 7,
  "intakeGuidance": "Assess the real remaining outcome; don't duplicate adequately tracked manual tasks.",
  "ignoredProjects": [],
  "ignoredLabels": []
}
```

Teams input:

```json
{
  "tenantId": "<tenant-id>",
  "clientId": "<public-client-app-id>",
  "clientSecret": null,
  "removeClientSecret": false,
  "pollingIntervalMinutes": 5,
  "debounceMinutes": 2,
  "intakeGuidance": "Capture my assignments and requested replies, not every team discussion.",
  "includeChatMessages": true,
  "includeChannelMessages": true,
  "includeCallTranscripts": false,
  "ignoredPeople": [],
  "ignoredChats": [],
  "ignoredChannels": []
}
```

Do not remove credentials by setting `removeApiToken`/`removeClientSecret` unless the owner intends disconnection. List updates replace lists; preserve existing entries. Relevant application permissions and Microsoft application registration are described in [deployment.md](deployment.md). Device sign-in is owner interaction, not something an assistant can consent to on their behalf.

## Gateway

Paths are relative to `/admin/setup`. Full contract lives in the [Gateway repository](https://github.com/MichalTichy/CodexCLI-API-Gateway/blob/main/docs/ai-setup-api.md).

| Operation | Request/result |
| --- | --- |
| `GET /state` | `{apiKeys:[{id,name}],projects:[...],mcpServers:[...]}`; secrets excluded. |
| `POST /api-keys` | `{id,name}` → `{id,created,key}`; only new keys return their secret. Use the provision helper to persist it privately. |
| `PUT /projects/{id}` | `{name}` ensures project; existing matching settings preserved. |
| `POST /mcp-servers/discover` | Full definition → metadata array `[{serverId,serverInfo,tools:[{name,description,inputSchema,annotations}]}]`. A null `serverInfo` is a failed discovery, not a working connection. |
| `PUT /mcp-servers/{id}` | Register definition; identical retry is safe, changed existing definition returns 409 unless `?replace=true`. |
| `PUT /projects/{projectId}/access/{keyId}` | `{apiKeyId:"<keyId>",webSearchMode:0,mcpServers:[{serverId,required:false,enabledTools:["exact_name"]}]}`. Replaces this key's assignments; preserve its existing server entries. |
| `GET /codex` | Account/login status. |
| `POST /codex/device-login` | Start owner-completed device sign-in. |

HTTP server definition:

```json
{
  "transport": "http",
  "id": "samwise",
  "name": "Samwise",
  "enabled": true,
  "executionMode": 1,
  "url": "http://web:8080/mcp",
  "environmentHeaders": {"X-Api-Key":"SAMWISE_MCP_API_KEY"},
  "environmentVariables": [],
  "availableTools": ["get_agent_settings","list_playbooks"]
}
```

Use discovery to populate reviewed tool names before registration/grants. `executionMode:1` means Gateway-hosted. Stdio definitions use `transport:"stdio"`, `command`, `arguments`, and environment variable **names**; the package/executable must be available inside Gateway, not only on the assistant’s machine. Planning grants should include Samwise lookups needed by its prompt and playbooks, including execution schema lookup where used. Never grant write tools solely because a tool is labeled safe by its provider.
