# Sequential scenario review

Reviewed against the uncommitted implementation on 2026-11-17 by a separate review agent. Each scenario is submitted separately; findings are fixed and reconfirmed before moving on. PASS covers the implementation mechanism, not a claim that live playbooks, Gateway grants or external sources have been deployed.

| # | Situation | Agreement and evidence |
|---|---|---|
| 1 | Kupkolo: delayed Wallet transaction, next check, deadline, human Todo escalation | PASS. Versioned task continuation, evidence and unresolved-run guard; normal approval for Todoist writes. Exact transaction identity and deadline belong in the delegated instructions/playbook. Live flow not executed. |
| 2 | Education: five missing contacts before seven sends; physical/manual handoff | PASS after aligning the default Planning prompt with the mandatory contract: ask for missing identities/facts/decisions after investigation, before partial execution. Handoff does not assert physical completion. |
| 3 | Notino: timesheet, owner send, client approval, invoice, IS approval, draft, owner send | PASS. Checklist evidence and next checks span interventions; each intervention retains normal planning/approval. Planning cannot silently terminate an unfinished task without a continuation. Real approvals/sends require evidence from available sources. |
| 4 | Partial failure, unknown writes, concurrent start and restart | PASS after fixes. First self-check blocks on its origin. A failed competing execution claim cannot overwrite the winner. Before startup accepts work, interrupted execution/invocations become OutcomeUnknown without replay. Owner reconciles upstream agent/effects before closing. Applies to one web instance. |
| 5 | Recurrence, DST, downtime, per-instance edits and template changes | PASS after recalculating the cursor when saving a changed cadence. Existing copies remain independent. UTC projection offset regression fixed and verified in UI. UI states materialization-on-open and disable/re-enable behavior. |
| 6 | Todoist delegation scope, undated children, old Intake and legacy work | PASS after fixing live-history ordering and rejecting conflicting task bindings. Explicit project OR label, exclusions, assignment checks and retirement of old inputs are verified. Configuration and sync share a lock. |
| 7 | Source edits vs internal checks, completed evidence and approvals | PASS after full-source fingerprinting (including deadline), review steps for changed completed work, custom-check preservation and full legacy snapshot comparison. Overdue import postponed before first run moves its implicit check. No atomic cancellation of an already-running upstream operation is claimed. |
| 8 | Recurring completion, postpone/undo, source deletion, 404 and outage | PASS after retaining monotonic counter evidence, restricting clean copies to proven new recurring occurrences, preserving checklist on restoration, inheriting unresolved origins across generations and preserving partial failures when access returns. API state regressions distinguish unavailable from completed. |
| 9 | Waiting-state removal, legacy migration, tool and grant boundaries | PASS after separating owner `save_agent_task` from required-WI `save_agent_task_progress`. Execution creation always inherits its executing origin. Raw migration preserves history and never runs work. Deployment grants and source cutover remain explicit steps. |
| 10 | Calendar/editor UX on desktop and mobile | PASS after making External sources visible on mobile and hiding ineffective time edits for custom cron. Final isolated-browser screenshots and interaction checks verify source-owned fields, Prague offsets, custom recurrence, Escape, visible Save footer and no horizontal overflow. |

All ten scenarios were agreed sequentially after the listed findings were fixed and rechecked. No live approval, external send, source cutover or Gateway-grant update was performed during review.

## Visual verification

Local disposable database and fake future examples only. Desktop 1440×1000 and mobile 390×844 were inspected. T3 screenshot capture began timing out while interaction checks remained usable; after the alternate browser plugin reported no available browser, final screenshots were captured in an isolated local Edge session and opened for visual inspection.

Artifacts are under `C:/Users/tichy/AppData/Local/Temp/samwise-tasks-visual-review/`: `calendar-desktop.png`, `calendar-mobile.png`, `source-editor-mobile.png`, `custom-routine-mobile.png`, and `todoist-source-mobile.png`.

Verification covered the mobile source link, preserved custom recurrence without an ignored time field, readonly source title/instructions, separate source due and next check in Europe/Prague, modal Escape and scrollable body with visible Save footer. The source settings page has no horizontal overflow.


## Final validation

`dotnet test tests/Samwise.Tests/Samwise.Tests.csproj --no-restore` with disposable PostgreSQL via `SAMWISE_TEST_POSTGRES`: 303 passed, 7 existing identity tests skipped, 0 failed. Web build: 0 warnings, 0 errors. `git diff --check` passed. Preview process, isolated browser and disposable database were stopped after verification.
