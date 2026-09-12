# Samwise V1 architecture

## Product boundary

Samwise is a personal, single-owner assistant—not a multi-tenant SaaS. V1 deliberately uses one deployable web process and one PostgreSQL database. Externally triggered MediatR use cases coordinate the short workflow through scoped services; there is no separate worker process, work-item queue, or separate processor. A small in-process timer is the external trigger for periodic email checks.

The web browser never calls the Codex CLI API Gateway. All Gateway credentials and calls remain server-side.

## Canonical intake

Each source has an external use case and creates its own typed inbound event:

```text
SubmitManualInputUsecase -> ManualInboundEvent
ProcessUnreadEmailsUsecase -> EmailInboundEvent
```

Both operations call `InboundEventCaptureService`, which uses `Source + ExternalId` for connector-level deduplication. A deterministic key has a database unique constraint. The service stores an `InboundEvent` in `Pending` state and calls the inbound-event processing service, which performs classification and, when the result is actionable, planning.

Manual web text stores the entered text on `ManualInboundEvent`. The IMAP adapter stores account and message metadata plus a normalized body on `EmailInboundEvent`; its stable external identity is account ID, mailbox, UIDVALIDITY, and UID. Each derived event computes its intake text from its source fields. Future Teams and Todoist sources follow the same rule. Connectors do not create or modify work items themselves.

Use cases represent external triggers only: a user action, an endpoint request, or a newly received input event. A use-case handler coordinates the integral steps of that operation by calling scoped processing, classification, planning, execution, and invocation-lifecycle services. It never sends another use case.

Reusable behavior belongs in services. Notifications are reserved for independent post-completion reactions that do not form part of the operation's required control flow; V1 currently needs no such notifications.

Use-case handlers and workflow services use the copied shared-infrastructure `IRepository<T>` and `IReadOnlyRepository<T>` abstractions. Named specifications live beside their consuming feature in `Samwise.Logic`. Blazor pages execute public specifications through `IReadOnlyRepository<T>` for reads and send use cases for operations. They never depend on Marten directly.

The intake prompt receives the untrusted input and a compact list of active work item candidates. Its strict structured result is one of:

- `CreateWorkItem` — create a new actionable work item, then queue planning;
- `AppendToWorkItem` — append context to an exact candidate work item, increment its revision, invalidate its current plan, then queue planning;
- `Ignore` — keep the event in the audit log without a work item;
- `NeedsReview` — keep the ambiguous/failed event visible without guessing; the Input events page can queue classification again after the prompt or transient problem is corrected.

The selected existing work item must be both in the supplied candidate list and still active when the result is committed.

## Work item and plan lifecycle

```mermaid
stateDiagram-v2
    [*] --> Planning: actionable intake
    Planning --> AwaitingApproval: plan generated
    Planning --> NeedsAttention: planning failed
    AwaitingApproval --> Approved: approve exact plan + work item revision
    AwaitingApproval --> Planning: reject with feedback
    AwaitingApproval --> Planning: new context
    Approved --> Executing: approval use case calls execution service
    Approved --> NeedsAttention: pre-execution validation failed
    Executing --> Completed: confirmed success
    Executing --> OutcomeUnknown: interrupted / uncertain outcome
    NeedsAttention --> Planning: retry
    Planning --> Discarded: discard
    AwaitingApproval --> Discarded: discard
    NeedsAttention --> Discarded: discard
    OutcomeUnknown --> Discarded: close after manual reconciliation
```

A `WorkItemPlan` is Samwise’s own serializable plan snapshot, not an opaque Agent Framework session. It is nested in its work item document and contains technical steps, referenced qualified tool IDs, side-effect flags, success criteria, assumptions, risks, the exact work item revision, the selected playbook snapshot when one applies, structured snapshots of both tool catalogs, the agent-configuration identity/timestamp, the planning invocation ID, and its optional review. The invocation document owns the full rendered prompt and raw AI response, avoiding duplicate large strings inside every plan.

Each plan owns at most one `PlanReview`, either `Approved` or `Rejected`, with its feedback, source, and timestamp. A rejected plan retains its review after the work item advances to a new revision and receives a replacement plan. Discarding is a work-item lifecycle action rather than a plan review, so `DiscardedAt` and `DiscardedFrom` live directly on the work item and an abandoned current plan remains unreviewed.

Approval is a compare-and-set operation against `(WorkItemId, PlanId, WorkItemRevision)`. Rejection feedback and added context increment the work item revision and make the prior plan non-approvable. Before execution, Samwise fetches the execution tool catalog again; a changed catalog fails closed rather than running a stale approval.

## AI boundary

`IAgentRuntime` has three structured operations:

- classify an inbound event;
- generate a whole-work-item plan;
- execute one approved whole-work-item plan.

The production adapter keeps three fixed Gateway endpoint/credential profiles:

| Profile | Credential | Purpose |
|---|---|---|
| Intake | planning key | projectless classification; no MCP tools |
| Planning | planning key | planning project; normally no tools or only tools safe for the planner to invoke |
| Execution | execution key | execution project; exact MCP tools allowed after approval |

The finalized Gateway catalog contains only tools enabled for the authenticated key/project; it
does not expose a separate visible-only set. Samwise therefore fetches the execution catalog with
the execution profile and inserts that catalog into the planning prompt. The planner can describe
those future capabilities without receiving the execution credential.

Each call uses `IAgentRuntime`, implemented by a stateless Microsoft Agent Framework `ChatClientAgent` over the official OpenAI Chat Completions connector. Endpoint URLs and credentials are provider-neutral `AgentEndpoints` configuration; they currently point to Codex Gateway. No Agent Framework or native Codex session is persisted in V1. This avoids two competing sources of truth: all continuation context is assembled from Samwise’s stored work item, comments, prior plan, feedback, and tool catalog.

The model and reasoning effort are not deployment-profile settings. They are stored as exact catalog identifiers in each nested stage of the single agent-configuration document and can be changed from the authenticated UI. Reasoning effort is not an enum: supported values and defaults are model-specific and currently come from the Gateway extensions on the selected endpoint's `/v1/models` response. The selected values are passed through the standard OpenAI runtime for each invocation; endpoint URLs and API keys remain server-side deployment configuration.

Intake and planning calls have bounded transport retries because their credentials cannot perform mutations. Execution disables automatic SDK retries: a lost response after an MCP side effect is marked `OutcomeUnknown` and cannot be edited or retried automatically. A failure known to occur before execution starts is `NeedsAttention` and can safely create a replacement plan.

## Agent and playbook configuration

There is one `AgentConfiguration` document containing fixed nested settings for Intake, Planning, and Execution. Startup creates the complete document; stage saves require that root and update one nested setting instead of creating database documents on demand. The authenticated Agent settings page loads the Gateway catalog, restricts reasoning effort to the selected model's advertised values, and edits each stage's prompt with a placeholder legend and save-time validation. V1 intentionally has no configuration version history or live preview.

Playbooks are independent UI-managed documents containing a name, description, enabled state, and update timestamp. During planning, all enabled playbooks are rendered through `{{playbooks}}`. The planning agent selects at most one exact playbook ID, or `null` when none clearly applies. Samwise validates that selection and stores the selected playbook's ID, name, description, and timestamp inside the generated plan. Playbooks steer planning only; they do not own Gateway credentials, tools, execution profiles, handovers, or a separate workflow lifecycle.

Every invocation stores its fully rendered prompt, so changing the current template does not erase what a past run saw.

## Email input

The authenticated Configuration → Inputs → Emails page owns the polling interval, a global list of exact ignored-sender addresses, and individual IMAP accounts. Automatic processing is always active; disabling an account removes only that account from scheduled checks. The same page can test an account and manually process one or all enabled accounts.

`EmailInputConfiguration` is a singleton Marten document. `EmailAccount` is a separate document per mailbox because its check timestamps and error state change independently. Passwords and app passwords are protected with ASP.NET Data Protection before persistence and are never returned to the UI; the persisted key ring is shared with authentication cookies.

`Samwise.Infrastructure.Email.Imap` uses MailKit, opens `INBOX` with read-only access, searches for unread messages, and never changes message flags. The dedupe identity is based on IMAP UID semantics rather than `Message-Id`. Attachment metadata is recorded without downloading attachment bodies. Exact ignored senders are recorded as ignored events without an AI call or body download. OAuth, sending, folders other than `INBOX`, and IMAP IDLE are outside V1.

## UI

- **New input** — manual capture form and a four-step explanation of capture, triage, planning, and approval.
- **Board** — Todoist-inspired columns/cards for active work item states, including draft planning and awaiting approval.
- **Input events** — chronological source text, pending/final handling decision, classifier explanation, linked work item, and retry for events needing review.
- **Work item detail** — current plan, steps, tools, assumptions, risks, approve/reject/discard controls, comments, plan reviews, and recent agent activity.
- **Agent settings** — model, reasoning effort, prompt editor, and placeholder legend for each stage.
- **Playbooks** — create, edit, enable, and disable reusable planning guidance.
- **Configuration → Inputs → Emails** — polling interval, exact ignored senders, IMAP account management, connection tests, and manual unread processing.
- **Account** — two Samwise-owned pages: password-only login and change password. Startup seeds the one owner from `OwnerAccount` configuration. There is no generated Identity UI, registration, email confirmation/recovery, external login, 2FA, or passkey flow.

Work item comments are first-class owned values inside the work item aggregate. This supports ordinary context and rejection feedback immediately, makes the work item revision and its new context one atomic document update, and lets a future Todoist projection treat comments as another command surface without changing the work item model.

## Future Todoist UI adapter

Samwise remains authoritative. A future adapter can mirror one Samwise work item into a dedicated Todoist board and store an `ExternalWorkItemLink` mapping. Plan snapshots can be posted as comments. Approval commands should be strict, revision-bound comment commands such as `/samwise approve <one-time-code>`, deduplicated by Todoist comment ID and routed into the same approve/reject/discard application commands as Blazor.

Ordinary Todoist comments become work item context and invalidate the current plan. Todoist completion must not implicitly mean approval.

## Persistence

- Marten schema `samwise`: versioned work item aggregate documents (including nested plans, selected playbook snapshots, plan reviews, comments, discard metadata, plan steps, and tool snapshots), polymorphic inbound events, email accounts, email-input configuration, agent invocations, the nested agent-configuration document, and playbooks.
- EF Core schema `identity`: the single password-authenticated ASP.NET Identity owner. Samwise uses Identity only for its password store and application cookie.
- Durable Data Protection keys: login cookies survive restarts.
- EF migrations initialize Identity; Marten creates or updates its document schema at startup. This is an intentional single-user V1 simplicity tradeoff.

The work item is the consistency boundary. It implements Marten numeric revisioning, so concurrent updates fail instead of silently replacing nested context or plan reviews. Each work item transition—including its comment, plan review, discard metadata, or plan—is persisted as one document update. Intake classification uses one ambient Marten transaction when it must update both an inbound event and a work item. Agent invocations remain separate because they are large operational audit records whose running/final states straddle an external call.

V1 assumes a single user and a single web process. It intentionally has no durable workflow queue: an external use case coordinates classification, planning, or execution services to completion in the same request. The in-process email poller triggers that same external email-processing use case. If durable background delivery or broader crash recovery becomes necessary later, it can be added around those services without changing the external use-case boundary or moving workflow rules into persistence.

## Code organization

The solution follows the useful Edifio conventions while retaining the single-process V1 deployment:

- externally triggered operations are explicit MediatR `*Usecase` request/handler pairs in `Samwise.Logic`;
- use cases coordinate integral follow-up steps by calling scoped application services directly;
- use cases never send other use cases, and behavior shared by multiple use cases belongs in services;
- notifications are reserved for independent reactions after an operation completes, not for mandatory workflow steps;
- Blazor pages use the copied shared `IReadOnlyRepository<T>` contract and public `*Specification` classes directly for reads;
- use-case handlers and workflow services use the copied shared `IRepository<T>`/`IReadOnlyRepository<T>` contracts for processing;
- specifications live beside their feature in `Samwise.Logic`; read-only `Get*Usecase` wrappers are intentionally absent;
- each infrastructure concern has its own project and `IInstaller` implementation;
- standard OpenAI execution lives in `Samwise.Infrastructure.AgentRuntimes`, while only nonstandard catalog discovery lives in `Samwise.Infrastructure.CodexGateway`;
- IMAP transport, credential protection, and the in-process polling trigger live in `Samwise.Infrastructure.Email.Imap`; sender-ignore and workflow decisions remain in `Samwise.Logic`;
- EF migration/owner creation and prompt seeding are `InitializerBase` startup initializers; prompt defaults and initialization belong to `Samwise.Logic`, not the Marten adapter;
- custom Blazor pages, including login and password change, keep markup in `Page.razor` and behavior in `Page.razor.cs`;
- `Program.cs` is only the composition/pipeline entry point and discovers installers and initializers;
- required Edifio shared-infrastructure projects are copied under `SharedInfrastructure` and referenced only by repository-local project paths.

`Samwise.Infrastructure.Persistence.Marten` now contains only its installer and document mappings. It registers the copied shared Marten repository implementation; it contains no use cases, prompt behavior, worker, or domain workflow orchestration.
