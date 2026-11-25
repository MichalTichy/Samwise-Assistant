# Set up Samwise with an AI assistant

Tell me where your work arrives and what you want help with first. I’ll suggest a small setup, handle the configuration and guide you through the sign-ins. Then we’ll try a situation from your day.

Give this guide to an assistant that can read the matching checkout and run local commands or connect MCP servers:

> Help me set up Samwise. Read docs/ai-setup.md in this checkout. Start with where my work comes from and what I want help with.

The rest of this document is instructions for that assistant. Read it before beginning; use the source checkout matching the deployed version. Never claim that unpublished code is available from GitHub or that an older image supports these setup APIs. The conversation below governs the user experience; the deployment and API sections are implementation instructions for you, not a questionnaire or checklist to paste into chat.

## 1. Start with the person

### Keep the conversation moving

Keep ordinary replies to a few short sentences with one clear next action. Ask for decisions, not acknowledgement of progress: perform independent authorized work between owner steps and report the result together with the next needed action. Prepare a sign-in before giving the owner its link rather than sending a separate announcement. Ask together about closely related facts needed for the same decision, such as the mail service and where tasks live; keep separate account sign-ins as separate actions. Reuse answers and accepted scope instead of recapping them at every step. Explain unfamiliar terms at first use, then use the everyday name. Brevity must preserve access scope, meaningful choices and honest limits.

In a rehearsal, establish once that all operational results are simulated, label fixture links and results, and repeat the limitation in the handover. Do not attach a full simulation disclaimer to every reply or imply a live connection.

### Understand their work

Begin with one short paragraph explaining the process in ordinary language, adapted from the opening above. Then ask **where work most often arrives** (Teams, email, another chat, a task manager, or elsewhere) and **which one or two kinds of work they would like help with first**. They need not know the product or invent a technical test. Offer a few everyday examples if useful. Match their language. Do not open with deployment status, URLs, models, Docker, Gateway, Intake, MCP, credentials, or a long intake form. Those details come after the proposed use is understood.

Follow their answer rather than running through every possible integration. Clarify only what changes the first useful result: the accounts/services involved, what counts as their responsibility, a meaningful exclusion, and a decision or review preference. Do not make them supply exact sample text, timestamps, tool names, IDs, a deployment recipe, or a list of permissions before you can suggest help. Use the normal timezone already supplied by the app, calendar, or environment without turning it into an onboarding question. Raise it only when concrete conflicting information would change the actual result, such as an explicitly different location or source time; a hypothetical possibility of travel or an unset optional field is not a reason to interrupt the user.

### Propose a useful first setup

Suggest the few connections needed for the selected outcomes, explaining the role of each in user terms. Distinguish **incoming information** from **tools for looking things up or changing things**. For example, following new Teams messages is different from giving Samwise a way to retrieve a conversation or save a task. You may name Intake and MCP parenthetically if the owner asks or already uses those terms; knowing them must not be a prerequisite.

Give a complete situation based on their answer: what arrives, how Samwise recognizes their responsibility, what it investigates, what it prepares or records, what the owner sees/approves, and what evidence makes the work finished. Include the important exclusion they mentioned. A source is not automatically an outbound destination. A calendar is needed for availability/scheduling, not for every email reply; sending permissions are not needed for draft-only replies. Reuse their preferred task manager instead of assuming Todoist.

Where commitments matter, explain completion across channels: a promise in Teams may be fulfilled by an email or a completed task. Samwise should compare the actual obligation and remaining work, retire a superseded proposal when fulfilment is evidenced, and use the agreed review policy for any task updates. Do not promise recognition of activity outside connected sources or automatic task completion without the corresponding tools and approval policy. Distinguish a desired scenario from a capability already verified on this installation.

Include the AI-account dependency with the first concrete proposal, before deployment or sign-in, rather than introducing a separate account questionnaire: Samwise needs an AI service to understand messages; this Gateway deployment uses Codex through an OpenAI account the owner may already use for ChatGPT. Explain that this is separate from their Samwise account and mailbox sign-in. Discover existing authentication first, and if new access is needed check whether they already have that account; verify usable model access after sign-in rather than making the owner know their plan's entitlements. Do not surprise them with another account only after installation, require an unnecessary new account, or promise access/cost before verification.

Check a decisive capability constraint before deploying for it. In this version, Teams input has exclusion lists (`IgnoredChats`, `IgnoredPeople`, `IgnoredChannels`), not an allowlist that keeps every new unselected chat unread. Do not describe this as “only selected chats.” Also check lookup-tool scope separately from incoming-source scope. If this changes the owner's chosen outcome, explain the concrete limitation briefly and let them choose a supported scope before configuration. Distinguish not generating tasks from private topics (content is still read) from excluding whole conversations from reading.

End the proposal by asking whether this direction fits and inviting a correction. **Confirm the proposed scope once before setting it up.** This is a choice of useful behavior and connections, not repeated permission for every configuration write. If their message already accepts a concrete proposal, proceed; do not ask them to confirm it again. If they change their mind, update the proposal and its dependent connections before asking for sign-ins.

If the owner asks to be reminded, agree where their attention will be drawn, not just where tasks are stored. Distinguish a Samwise proposal waiting for review from a task deadline and a timed reminder. Inspect the deployed notification settings and the task tool's exact reminder support. Explain the actual path in ordinary terms; do not imply that a due date sends a notification or that the owner will notice an unannounced proposal. If available, guide the owner through enabling notifications and verify a harmless notification on the intended device. Otherwise explain the manual dashboard check or missing capability and confirm whether that meets the need. Do not introduce a reminder-time question unless a timed reminder is actually wanted.

### Set up the agreed scope

After the direction is agreed, establish what is already running and where to install missing components. Explain Samwise and Gateway briefly only when this decision becomes relevant. A nontechnical owner can say they are starting from scratch or want help choosing; do the discovery and propose a practical deployment. Inspect available management tools and authorized existing configuration before asking for information you can retrieve. Do not silently assume the assistant's computer is the user's intended host. Existing installations should skip unnecessary deployment.

For a new installation, check whether Docker is available in the accessible environment before asking the owner to investigate it. Distinguish an installed CLI, a reachable engine, Compose support, and the engine's actual target (local Docker Desktop/Engine or a remote context). Report only what you observed. At its first mention to an unfamiliar owner, explain Docker in one short sentence as the program that runs Samwise and its helper services. Recommend the practical option supported by discovery. Offer to run the stack in their local Docker or prepare a Compose deployment for their chosen server/NAS; Compose can also run locally, so these are deployment destinations, not competing technologies. If they do not know, help choose from the available environment and whether monitoring should continue when their computer is off. Ask only for the unresolved destination or preference; do not make Docker expertise a prerequisite. Discovering Docker is not authorization to start containers on that engine.

For a new installation, let the owner create their account in Samwise rather than asking for their email or generating their password in chat. After Samwise is running, open the private one-time registration link in their browser (or give an accessible clickable link if opening is unavailable), say that they should create their Samwise account there, and wait for completion. The form asks only for email, password and password confirmation; this account is separate from mailbox/provider access. Never fill the credentials on their behalf, retrieve their password, or treat merely opening the page as completed registration. Existing owners go straight to sign-in.

Before the first sign-in, give a short preview of the accounts still to connect and mention any separate mailbox lookup/draft consent, using discovered requirements. This is orientation, not another confirmation round.

Carry out the technical steps in sections 2–6. **Before each owner sign-in, permission request, token entry, or manual action, say what it enables and why this agreed scenario needs it**, then give one concrete next step. Explain material access scope and any sending/writing ability plainly. For example: “To notice requests sent to your work mailbox, I need you to connect that mailbox. This step lets Samwise read incoming messages; it does not send replies. Open this sign-in page…” If a second connection is needed for investigation or saving drafts, explain that separate purpose instead of implying the first sign-in configured everything.

Generate internal Samwise/Gateway keys yourself, store them privately, configure both sides, and verify the saved credentials. Do not turn internal wiring into owner homework. If another account consent, administrator action, paid service, or unsupported capability is required, explain that specific dependency and the useful alternatives. Do not ask the user to paste passwords or tokens into the conversation. A failed sign-in is not a reason to invent a successful connection, repeatedly restart login, or quietly remove the chosen source. Keep the pending step explicit, continue independent authorized work, and resume from verified state when access arrives.

Use short outcome-based progress updates, such as “New Teams messages are connected; next I’ll connect your task list so Samwise can keep track of the commitments you make.” Reserve endpoint names, IDs, logs, model catalog details, and test-harness coordination for the private progress/evidence record unless the owner needs them to act or asks for them. Preserve the distinction between agreed behavior, configured connections, and a result actually tested.

### Try it and invite the next scenario

Use the agreed situation for the first test and suggest the sample yourself; ask only for facts or decisions that genuinely affect its meaning. Let the owner choose the review boundary before external effects. Inspect the actual proposed plan, verify the result, and test a nearby example that should produce no work. Do not substitute a local fixture for a chosen real connection without the owner's choice, or present a simulation as proof of live ingestion or delivery.

When proposing a trial that writes anything, explain the small test and its cleanup together. Use only the agreed test artifacts; an accepted bounded trial and cleanup should not need another permission round when the actual plan matches it. If a granted tool cannot remove a test artifact, explain the specific owner cleanup step before creating it instead of broadening access. Complete immediate test cleanup before a long handover or an invitation to add scenarios, while keeping setup access available for accepted additions. An owner who only asked to see a proposal has not authorized an external write.

Keep each manual-step message focused on one immediate action and its short reason. Avoid restating the entire agreed configuration at each sign-in. At handover, separate a pending cleanup action from the result summary and next-scenario question; do not bundle all of them into one long message.

At handover, say where to use Samwise, which agreed situations now work, what was actually tested, and any precise unfinished step. If setup is paused or incomplete, name the first concrete next action, who performs it and what result will let you continue; “finish deployment and connect accounts” is not an actionable handoff. In a rehearsal, label fixture URLs as simulated and never present them as an application the owner can actually use. Mention occasional [Samwise tune-up](../skills/samwise-tune-up/SKILL.md) in one practical sentence: after a few days, bring missed promises or unnecessary tasks so the rules can be adjusted. Link the skill the owner can open in their checkout or installed skills; do not substitute an unverified public link for an unpublished skill. Expand on tuning only if asked. Do not schedule tuning or imply configuration changes are preapproved by mentioning it. Keep this first handover short: result and tested boundary, any next step, tuning link and invitation. Put technical verification details in the progress record.

**Then explicitly ask whether any other situations come to mind that they would like Samwise to handle.** If they suggest one, map it to existing connections first and add only what it needs, following the same propose → agree → configure → verify flow. A request for another scenario does not authorize sending, invitations, or unrelated account access. If they are done, finish without restarting the onboarding questionnaire.

Make that invitation after the first result, while the active setup session can still handle an agreed addition. Close temporary administration access once the owner is done adding scenarios; also close it when pausing for unavailable access or an owner who is away. Do not keep it open for an indefinite future reply. If it was already closed, report and securely reopen only what an accepted addition needs, rather than asking the owner to repeat the whole setup.

Record decisions and unfinished steps in an ignored `.data/setup/progress.md` so another session can resume. This record is technical memory, not text the owner must compose.

Preserve concrete choices in the progress record, including timezone, reminder time versus deadline, review preferences, and the selected test. When a later answer changes a choice, update the actual sample and handoff procedure together; do not verify against an earlier invented default. Before execution, compare the generated arguments with the latest owner decision.

Keep permission proportional to the action. If the owner already authorized a bounded, reversible local demo and its cleanup, inspect the generated plan against that scope and complete it without asking the same permission again. This does not enable automatic approval in Samwise or authorize actions against real accounts. Use the normal plan review operation; ask only if the plan goes beyond the authorized result or the owner chose to review it personally.

Useful starting points, rather than a mandatory bundle:

| Desired outcome | Input and evidence | Useful execution capability |
| --- | --- | --- |
| Turn actionable mail into reviewed drafts | Email input plus mailbox lookup | Save a draft; sending is a separate choice |
| Arrange meetings or record confirmed reservations | Mail/Teams/manual input, calendar lookup | Create or update events after review |
| Remember parcel pickup | Mail/manual input | Create a task with collection deadline |
| Handle future checks and follow-ups | Todoist due input | Maintain tasks; optionally a dedicated deferred-work project |
| Recognize completed obligations | Relevant mail/chat/task evidence | Complete the exact verified task |

These patterns are informed by a mature installation. Do not copy its private prompts, contacts, account IDs, business systems, or transaction history. A new installation should express the new owner’s goals.

## 2. Establish access and choose a deployment path

Inspect available tools first. Prefer an existing Samwise/Gateway/provider management MCP over recreating its API or asking the user to click through settings. Use the [setup MCP bridge](#temporary-setup-mcp) when the assistant supports stdio MCP; the same operations are available through `scripts/ai_setup.py`. For externally hosted MCP servers, use the provider’s own authorization instructions and tools. Use a provider UI only for an operation that actually requires it, such as account consent or an unavailable administrative capability; explain that specific step.

For existing deployments, obtain URLs, version/build provenance, and the deployment configuration location. Read current settings before editing. Preserve the existing owner identity and discover nonsecret values from the authorized configuration instead of asking the user to reenter them. Reuse databases, projects, keys, volumes, networks, input checkpoints, and playbooks. Do not run new-install preparation against an existing deployment or rebuild it without need. If setup APIs are missing, build/update to a revision containing them, or use its existing management tools; explain the concrete gap.

Never request passwords or tokens in ordinary chat. Use the assistant’s secure secret-input facility, a local protected file, or a user-completed login. Generate application credentials locally. Do not print `.env`, `docker compose config` with resolved secrets, tokens, auth caches, or full HTTP traces. Remote management requires HTTPS or a localhost tunnel. Setup keys are separate from inference, MCP, and manual-input keys and are never granted to agent runners.

### New deployment

First inspect Docker without changing its configuration: check CLI availability, `docker context show`, the selected context's Docker endpoint, `docker version`, and `docker compose version`. Inspect only necessary fields; do not dump credentials or unrelated container configuration. Honor explicit Docker environment overrides when identifying the engine target. A CLI alone does not establish a running local engine; an unreachable engine may simply need Docker Desktop started. An assistant running on a remote machine must not describe its engine as the owner's local Docker. If Docker is unavailable or the intended host is inaccessible, explain the finding and help choose/install Docker or prepare Compose files for that host. Do not switch contexts, start services, or deploy to a discovered remote engine without resolving the intended destination.

Clone Samwise and [Gateway](https://github.com/MichalTichy/CodexCLI-API-Gateway) as sibling directories. Use the companion setup branches listed in the root README until both APIs are merged into their default branches. Check Python 3.11+ (or use `uv run` for Python); source builds happen in Docker and need no host .NET installation. Agree on the target host and persistent storage before starting containers there. For a local installation, run the same Compose stack against the agreed local engine. For a server/NAS, prepare the configuration for that host and deploy through authorized access, or explain the remaining handoff if access is unavailable. A local test can use different host ports, a unique Compose project and `--deployment-id` to avoid existing instances.

From the Samwise checkout, prepare credentials and a local PostgreSQL instance:

```bash
python scripts/ai_setup.py prepare --local-database --deployment-id samwise --gateway-source ../CodexCLI-API-Gateway
```

The command generates a dedicated owner-registration token, not an owner email or password. It writes missing service credentials to ignored `.env`, preserves existing credentials, and selects locally built image tags. Confirm the Gateway checkout contains its setup API before building. Create `.data/mcp-files` for shared files. On Linux, set `DOCKER_GID` to the Docker socket’s group. Follow the Gateway repository’s runner AppArmor instructions only if the host requires them.

```bash
docker compose -f compose.yaml -f compose.local.yaml -f compose.build.yaml build
docker compose -f compose.yaml -f compose.local.yaml -f compose.build.yaml up -d
```

For public/reachable prebuilt images, omit `--gateway-source` and `compose.build.yaml`, and set image variables to versions the user can pull. The historical default registry may require access; a failed pull is a reason to use source builds, not to tell the user they must obtain private registry credentials. `compose.local.yaml` is optional when PostgreSQL is already provided: create the separate databases and configure both connection strings instead. Run initialization SQL only on a fresh database; never reset an existing volume to fix setup.

Once the new web application answers, generate its private registration link using the URL reachable in the owner's browser:

```bash
python scripts/ai_setup.py --samwise http://localhost:8080 registration-link --output .data/setup/owner-registration-link.txt
```

The URL must be HTTPS or localhost (use a tunnel for a remote host without HTTPS). Open the saved link for the owner without dumping the env file. Explain: “Samwise is running. Create your account here; then we'll connect the services we chose.” The token only permits the first owner registration; database locking prevents concurrent creation of multiple owners, and registration is unavailable once an owner exists, including after restarts. Wait for the owner to finish and confirm sign-in works. Clear `SAMWISE_OWNER_REGISTRATION_TOKEN` from the actual deployed configuration and remove the temporary link file after registration. Preserve the owner's database identity; no email/password needs to be copied back into `.env`. Legacy `--owner` provisioning remains for explicit unattended deployments, not the guided default.

For existing deployments, enable `Setup__ApiKey` independently on each application using fresh random values in private configuration and restart only the relevant application. With this repository’s Compose file the variables are `SAMWISE_SETUP_API_KEY` and `CODEX_GATEWAY_SETUP_API_KEY`. The helper can use a separate ignored env file (`--env .data/setup/connection.env`) containing only setup credentials and the existing profile keys. Do not replace deployed profile keys with placeholders.

## 3. Connect Gateway and Samwise

Commands below use default local URLs. Put global options **before** the command, for example `python scripts/ai_setup.py --gateway http://localhost:15050 --samwise http://localhost:18080 call gateway GET /state`.

1. Check `/health` on both applications, then read Gateway setup state. Fix any version/authentication/reachability error before continuing.
2. Inspect existing Codex authentication through `call gateway GET /codex`. If login is needed and the owner is ready to sign in, use `call gateway POST /codex/device-login`; present the current verification link/code and expiry in the owner's timezone. Do not start a short-lived challenge while the owner is away or restart an unexpired pending challenge. Continue independent configuration while waiting. Check once when the owner reports completion; if it is still pending, use a bounded wait and retain a resumable checkpoint rather than repeatedly asking or starting another login. Never copy the assistant’s own auth cache or substitute a fake model run.
3. On a new installation, run `python scripts/ai_setup.py provision`. It creates separate intake, planning, and execution keys and the two project scopes, writing each new secret directly into the private env file. Re-running preserves keys and grants. Existing key IDs without a recoverable secret require explicit recovery/new-key selection, never automatic rotation. On existing installations, first test and reuse the deployed profiles/credentials; use `provision` only for missing standard profiles. Do not overwrite custom working profiles simply to adopt the example names.
4. Recreate Samwise so it loads the saved keys: use the same Compose file list/project/env-file as deployment, with `up -d --no-deps web`. In another hosting environment update its secret configuration and restart that service. For split hosts, write the keys into the actual Samwise deployment, not merely the assistant’s local file.
5. Read `call samwise GET /status`. Check all selected profile URLs and credentials, including trailing `/v1/`. Discover model/effort options with `GET /agents/{stage}/models`. Preserve working choices on an existing installation; otherwise choose a sensible supported starting point yourself from the available access, latency and quality tradeoffs. Do not ask an unfamiliar owner to select a model ID or approve routine effort settings. Discuss the choice when they ask, have a concrete speed/quality preference, or a material access/cost decision requires it. Never hard-code model IDs from somebody else’s instance.
6. Save Intake, Planning, and Execution with `PUT /agents/{stage}` and `{ "model": "<advertised-id>", "reasoningEffort": "<advertised-effort>" }`. An omitted `prompt` preserves the installed prompt. Read `/agents` before adapting prompts; keep valid placeholders and the distinction between classification, read-only investigation, and approved execution.

Use the [management API reference](ai-setup-api.md) for request bodies. Store request JSON in ignored `.data/setup/` and execute it with `call ... --body path.json`; credential fields can reference `{ "$env": "PRIVATE_VARIABLE" }`.

To create only a missing standard profile, use for example `provision --profiles EXECUTION`; the MCP provisioning tool accepts the same `profiles` selection. Existing custom profiles should be left intact and use their existing project URLs and keys.

Automatic approval is optional and off initially. Configure it only for the owner's selected eligible outcomes: `provision --automatic-approval`, a separate approval profile with grants limited to review, a restricted auto-execution profile, models for both, and explicitly eligible playbooks. Inspect the actual catalogs; tools exposed by the approval profile, including Gateway's native workspace shell, do not block assessment. Samwise records the approval catalog for audit and requests only a decision and reason; it does not attach tool definitions to that inference request. Execution still requires every planned tool and its exact schema in the restricted profile. Test an approval, refusal, missing restricted capability, and manual fallback. Older Samwise builds reject approval profiles with tools and need updating to use this behavior.

## 4. Connect the selected inputs and MCP tools

For each chosen integration, finish one vertical slice before adding the next:

1. Inspect/reuse its existing connection and its MCP tool schema. Discover any missing server’s capabilities. Support both a remote HTTP endpoint and a Gateway-managed stdio server. Do not assume a new container is needed for an already deployed server.
2. Obtain provider authorization using its existing tools/flow. For a server that needs its own process, use its documented image/package and persistent storage. Gateway-hosted stdio is often sufficient without another container; HTTP services must be reachable from Gateway. Register one known version and test discovery.
3. Keep provider secrets in that server’s configuration or Gateway environment as appropriate. Gateway HTTP `environmentHeaders` maps header names to **environment variable names**, not literal token values. Adding an environment variable requires updating/recreating Gateway; a variable in the assistant’s shell alone is insufficient.
4. Register the server and discovered `availableTools` through Gateway setup. Review exact grants: intake/planning get the specific non-mutating evidence tools they need; execution gets the chosen action tools. MCP annotations are hints, not proof of safety. Read schemas and descriptions. Granting a server must not grant every future tool automatically. Access PUT replaces that key’s assignment list, so preserve its other existing assignments.
5. Register Samwise itself as `http://web:8080/mcp` (or its reachable deployment URL), execution mode Gateway, header mapping `X-Api-Key=SAMWISE_MCP_API_KEY`. Grant planning lookups including playbooks, input/work-item evidence, and exact execution-tool schema lookup. Grant Samwise configuration mutation tools only if the owner wants reviewed self-tuning.
6. Configure the input separately when required: email polling with its source guidance, Teams selections, or Todoist due-window/project filters. Use the existing input use cases through setup API, preserving checkpoints and ignoring no selected source silently. Generic IMAP needs mailbox credentials; Microsoft 365 and Teams use user-completed device login/consent. Todoist input is direct API ingestion and independent of its MCP task tools. Filesystem, memory, and Playwright start in default Compose but still need discovery and selected grants.
7. Test a read-only provider lookup, then show the owner what a proposed write would do. Do not send messages, invite attendees, unsubscribe, or modify real tasks merely to test the connection.

If a provider is not accessible yet, record exactly what is missing and finish independent configuration. Say “core ready; calendar waiting for consent,” not “everything is set up.” Configuration writes requested during setup are authorized within the selected scope; external business actions still follow the user’s review choice.

## 5. Write playbooks together

Create only the playbooks the user selected. Each should state its trigger and exclusions, evidence to retrieve, outcome and completion condition, decisions to ask the owner, specific allowed action, and handling of duplicates, missing access, or uncertain execution. Keep general behavior in stage prompts and scenario rules in playbooks. Do not turn every informational message into a task.

Read current `/playbooks` before saving. Match by existing ID; show a brief behavior summary and update by `playbookId` so reruns do not create duplicates. A POST without ID creates a new playbook. Initially set `suitableForAutomaticApproval:false`. Keep guidance independent of invented IDs or tool names; use actual discovered capabilities and verified account identities. The [starter examples](setup/playbook-examples.json) are discussion material, not a mandatory import.

Adapt source guidance to identify whose mailbox/chat/tasks these are and which personal obligations matter. Ask the owner to verify any ambiguous ownership. For deferred work, distinguish a not-before time from a deadline and verify the input’s lead/overdue windows; do not promise a future trigger the application does not provide.

## 6. Verify the experience and hand over

Do not declare success from health endpoints alone. Complete these checks and record real evidence in `.data/setup/progress.md`:

- The owner created their own account through the one-time registration page and can sign in, or the existing owner can sign in. Their password stays in the browser/Identity password hash, never in the assistant’s files or chat. Registration is unavailable after creation; remove its temporary token/link without changing owner identity.
- The actual saved keys can read their Gateway model and tool catalogs. Intake has no external MCP tools by default; the current Gateway also advertises its isolated workspace shell. Planning has reviewed evidence tools and may use native workspace processing for investigation; execution can see the intended action tools. Native shell networking is disabled by the runner sandbox, not by a prompt. A projectless intake key must not inherit external write access. Automatic approval, if chosen, must pass the configured eligibility, exact-plan and restricted-execution checks; tools exposed by the approval profile do not themselves prevent assessment.
- Selected inputs show the intended account and scope and can perform a non-mutating connection check. Explicitly identify any provider still waiting for consent.
- Submit one harmless, clearly marked manual example via `POST /api/manual-input`, with `X-Samwise-Input-Key` read privately. It must return 202 and proceed through actual model inference into a classified input and reviewable plan or focused planning question. Inspect `/api/setup/input-events` and `/work-items` (or Samwise MCP). Check the selected playbook and actual proposed result with the owner.
- If reminders or notifications were selected, verify both how a new review proposal reaches the owner and any promised timed task reminder. A saved date, subscribed browser or successful send request alone is not proof of delivery; record owner-observed delivery, or state the exact untested/missing step.
- Try one informational example that should be ignored. Explain any ambiguity and tune based on evidence. A failed/ignored actionable smoke test is not a pass.
- For execution, use a reversible test task/calendar or local test file within the owner's approved test scope. Inspect the actual generated plan and exact arguments before approving it through Samwise; existing authorization of that bounded demo can cover this step. Otherwise show the plan and obtain the missing approval. Verify the actual result, then clean up only the identified test artifact. If execution cannot be exercised, report “planning verified; execution not tested” and the remaining step. Do not use fake accounts or mock model responses as proof of a live setup.
- Restart only the intended services and confirm saved configuration remains. Preserve backups and volume identities. Leave unrelated services untouched.
- Once the owner has finished adding scenarios, or the session must pause, blank both setup key values in deployed configuration, recreate the applications, and confirm setup routes return 404. Removing a local key file alone does not disable server access. Disconnect the temporary setup MCP. Samwise’s ordinary MCP remains available according to its own grants.

The helper's `verify` command exercises Samwise's deployed model access and prints each profile's granted tool names without secrets. Use `submit --text-file .data/setup/example.txt` to submit an owner-selected example without handling keys in command arguments. These checks supplement the end-to-end criteria above; they do not by themselves certify planning or execution.

On Windows, the helper emits UTF-8. Set `PYTHONIOENCODING=utf-8` for any custom Python inspection scripts as well, so printing Czech results does not fail after a successful write. If a client fails after a mutation, read the saved state before retrying it.

When testing through the owner UI, wait for the interactive connection before clicking; server-rendered controls can appear before their handlers are connected. After approval, require a persisted plan review or a changed Work Item status before leaving the page. A click alone is not execution evidence. During rehearsals, use a new ignored evidence directory or append to the existing transcript; preserve earlier conversations for the final UX review.

Give the first-result handover and invitation from section 1 before final closure: usable URL, configured and tested scenarios, precise pending steps, the benefits and timing of [Samwise tune-up](../skills/samwise-tune-up/SKILL.md), and a direct invitation to add other scenarios. Once the owner is done, close temporary access and give a short final status; do not ask the scenario question again after they have already finished. Do not promise unattended monitoring or schedule tune-ups unless requested.

## Temporary setup MCP

The bridge uses the [official MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk/tree/v1.x) and reads private credentials from the same env file as the command-line helper. Add this stdio server to the **owner’s setup assistant**, using absolute paths:

```json
{
  "mcpServers": {
    "samwise-setup": {
      "command": "uv",
      "args": ["run", "/absolute/path/Samwise-Assistant/scripts/setup_mcp.py", "--env", "/absolute/path/Samwise-Assistant/.env", "--gateway", "http://localhost:5050", "--samwise", "http://localhost:8080"]
    }
  }
}
```

It exposes `setup_status`, `provision_gateway_profiles`, `manage_setup`, and the guide resource. If the assistant’s own Gateway hosts MCP, it can register this stdio server there in a dedicated owner-controlled setup project. Never add it to the Samwise planning/execution/auto-approval projects: it can administer their permissions. Its env file must exist on the machine running the bridge. Source deployment and provider sign-in still use the assistant’s shell and user interaction.
