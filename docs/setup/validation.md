# Guided setup validation

Validation performed on 2026-11-15 against local source builds of Samwise and its separately maintained Gateway. This records actual results, including unfinished verification; a healthy container is not an end-to-end pass.

Subsequent change: at the owner's explicit request, Samwise no longer rejects automatic approval merely because the approval profile advertises tools. The tool-free-profile limitations below describe the earlier rehearsal build. Current setup guidance is in [ai-setup.md](../ai-setup.md); restricted execution capabilities, plan/revision binding, and playbook eligibility remain required. The guard removal was deployed separately to the owner's instance on 2026-11-15 using its existing production base plus that patch only. Container health, ordinary MCP reads and preserved agent settings were verified; no live approval or historical work-item replay was triggered. This deployment does not publish or deploy the local setup workflow changes.

## Automated checks

- Samwise: 253 tests passed, including 8 HTTP setup tests covering disabled/authenticated access, credential redaction, invalid stages, and prompt preservation during model selection.
- Gateway: 3 new HTTP/PostgreSQL setup tests and 45 existing management, access and architecture checks passed. Tested credential issuance, repeat requests, scope isolation, collisions, exact grants, and redacted state.
- Setup helper: 11 tests passed, including clean preparation, credential preservation, selective recovery of missing project access, external-database protection, HTTP management restrictions, the distinction between isolated native shell and external MCP grants, stale login-code removal, actual HTTP 202 validation, and inspectable grant metadata without credential disclosure.
- Official MCP client: initialization, tool discovery, guide resource and setup reads passed; raw key creation outside the private provisioning path was rejected.
- Both skill definitions passed the skill validator.

## Fresh-agent rehearsals

The first fresh assistant held a goals-first conversation with a simulated owner and built an isolated local stack from both repositories. It configured separate inference credentials and projects, verified owner sign-in, saved four Czech playbooks, and registered Samwise, filesystem, memory, Playwright, and a clearly labeled local demo integration. Exact planning and execution grants were checked, and playbook IDs and owner access survived a Samwise restart. Demo mail/calendar/tasks were synthetic and made no real provider claims.

Reading the complete conversation and artifacts exposed these issues, which were corrected:

- Example database strings incorrectly blocked clean preparation.
- Repeating source-build preparation replaced explicit image tags.
- Playwright required its complete Docker hostname and port in its allowed-host list.
- Windows output encoding could fail after a successful Czech configuration write, making a retry ambiguous.
- The conversation asked for setup authority a second time after the owner had already authorized the scope.
- Existing-deployment setup unnecessarily asked for an owner address already present in authorized configuration.
- A later owner choice of reminder time was not reflected in an earlier prepared sample. Both sample and handoff now preserve the latest choice, and the guide requires checking generated arguments against it.
- Verification incorrectly equated “no external MCP grants” with “no native tools.” The catalog remains honest; isolated workspace shell is distinguished from external capabilities. Automatic approval retains its stricter guard and is not advertised as usable on a Gateway that cannot supply a genuinely tool-free profile.
- A resumed flow treated every test execution as requiring fresh permission, even when the owner had already authorized the precise reversible local test and cleanup. The guide and setup skill now carry that authorization forward while retaining normal Samwise plan review and checking actual arguments against scope.
- The helper hid entire `apiKeys` and `apiKeyAccess` collections, preventing inspection of IDs and existing grants. Redaction now retains that metadata and readiness booleans while continuing to hide actual credential values, including nested credentials. Verified against the live Gateway state as well as a regression test.
- Completed device-login responses still printed an old code. Terminal login codes are now omitted; pending login codes remain usable. The guide starts challenges when the owner is ready and preserves unexpired ones on resume.
- A rendered approval button did not imply that Blazor was interactive. UI tests now wait for the connection and require a persisted review/status change; a click alone is not reported as successful execution.
- The final live shutdown check exposed an integration bug missed by the original isolated HTTP tests: Samwise's HTML status-code middleware re-executed disabled setup API responses through an owner-only page, producing a login redirect instead of raw 404. Setup requests now opt out of status-page re-execution. The HTTP tests include host-style re-execution middleware and verify the original 404/401 codes survive it. All 253 Samwise tests passed after this fix, and the local web image was rebuilt for the live shutdown recheck.

A second fresh assistant followed the revised guide for existing deployments. Its real MCP bridge session succeeded. It independently verified byte-identical preparation retries, unique deployment resource names, valid Compose configuration, preservation of custom profile URLs/keys and unrelated settings, and no changes to the running instance during its inspection. It avoided repeating the setup-authority question. Private evidence stays in ignored rehearsal directories rather than being published with the owner's data.

After actual owner authentication, further fresh assistants exercised the existing-deployment path with real model inference. The calendar pass checked a busy interval, selected the authorized fallback, executed through normal owner review, verified the actual local event, and removed only its returned artifact ID. Its informational counterexample created no work. The subsequent pass used the updated authorization guidance for a prewritten unsent draft and a future follow-up task, preserving existing settings and asking no redundant owner questions.

The complete current rehearsal dialogues and artifacts were reviewed. One evidence-handling mistake was found: an agent initialized a transcript in a directory that already contained a previous preparatory pass, overwriting that earlier transcript. Its other preparation/preservation evidence and historical parent-thread result remain available. The new session records this limitation explicitly, and the guide now requires a new evidence directory or append-only continuation. No deployment data was overwritten.

## Authenticated local end-to-end evidence

The owner completed the isolated Gateway's real Codex login. Advertised model catalogs were read before selecting `gpt-6.1-sol`: low effort for Intake and medium for Planning/Execution. All saves succeeded; the deployed credentials passed model/tool verification. No mock model responses or assistant auth caches were used.

- Parcel: input `05ca56c7-37eb-4a8a-9359-0bc4d8629c9b` created Work Item `f6f63dd4-a4f9-4687-a0f9-aa3ff98347d3`. Real planning selected the parcel playbook and preserved Thursday 19 November 18:00 as reminder time, Friday 20 November 18:00 as collection deadline, and Praha Anděl as location. Normal owner UI review led to successful execution invocation `21349e02-0020-49eb-b2d2-c189c371ae0e`. Artifact `samwise-rehearsal-1-demo-1` matched every approved parameter, including description and uniqueness key; exact cleanup and absence were verified.
- Grocery noise: input `06cc7859-2377-4098-9b61-aa3b79aed7ea` was processed as informational, with no outcomes or additional Work Item.
- Calendar: Work Item `a63dea05-25ec-4efe-8c22-144c28e62a59` selected 17 November 11:00–11:30 after detecting the requested 10:00 interval was occupied. Execution invocation `8492fc57-2f12-47ae-818f-b8578616414f` succeeded. Artifact `samwise-rehearsal-1-demo-2` matched the exact title/times/key, was deleted by its returned ID, and its absence was verified with the original busy fixture preserved. No invitations were sent. Informational calendar input `b53d7ad9-3b7b-474b-a61f-0e7cb1c41c75` produced no outcomes.
- Unsent draft: Work Item `1fb07ccd-e5b2-47be-96a7-dc38721ecd60`, execution invocation `e16abe4f-0bf4-491f-8658-f9e915f9db7b`. The saved recipient, subject, and body matched the owner's exact text. Artifact `samwise-rehearsal-1-demo-3` was verified and deleted by its returned ID; no email was sent. A null playbook was valid for this prewritten persistence test rather than a request to compose a reply; no forced prompt/playbook change was made.
- Follow-up: Work Item `59f05eaf-f2b8-4242-87d9-35a33dee58b1`, execution invocation `c8b5c5c4-27cf-47f3-917d-72b5af2b709c`. The future-check playbook was selected. Artifact `samwise-rehearsal-1-demo-4` matched the exact title, `2026-11-19T09:00:00+01:00`, description and uniqueness key. Exact cleanup and absence were verified. Plan and result correctly said the demo does not trigger future Samwise work or notifications.

All four actual executions used normal owner UI approval under the owner's bounded demo authorization; automatic approval was not enabled. Final demo storage has no tasks, drafts or events. Prompts/models, playbooks, inputs and Gateway state were compared with their pre-test snapshots and remained unchanged.

## Final handover checks

Both setup keys were blanked in the actual isolated deployment configuration. Only that project's web and Gateway applications were recreated, with the original Compose file list, databases, auth volumes and networks. Following the status-page fix, raw requests to Samwise `/api/setup/status` and Gateway `/admin/setup/state` returned 404 both without a credential and with a credential header; neither redirected to a login page.

After recreation, exact agent settings/prompts/models and all four playbooks matched their snapshots. Existing runtime profile credentials matched their earlier hashes, all three profiles could still discover their configured model, ordinary Samwise MCP worked, and owner login succeeded. All demo task/draft/event lists were empty. Temporary MCP bridge subprocesses had exited; no administrative bridge was registered in runtime projects.

A final harmless informational input was accepted with actual HTTP 202 after setup access had been disabled. Its completed classification and the persisted Work Item list were checked through ordinary MCP, verifying the normal path remains usable without setup credentials. Private closing evidence is in `setup-closure-result.json` and `post-restart-informational-result.json` in the ignored first rehearsal directory.

## Scope and remaining provider work

These are live model runs against explicitly synthetic LOCAL DEMO mail/calendar/task storage. They verify the guided installation/configuration workflow and local planning/execution, not real IMAP, CalDAV, Todoist or Teams connections. Those accounts were outside the selected rehearsal scope and still need provider credentials/consent and their own read/write verification. A future `dueAt` in local demo storage neither delivers a notification nor schedules Samwise ingestion. Automatic approval remains disabled on the tested Gateway's native-shell catalog. These tests preceded publication. Use the companion setup branches listed in the root README until the matching changes reach both default branches.

## Publication checks

Before pushing the setup branches, Samwise passed 263 non-database tests plus all seven owner-registration integration tests against fresh disposable PostgreSQL (270 total). The helper passed all 13 tests. Gateway passed 62 of 63 end-to-end tests in the first run; the remaining test encountered a local HTTP-listener port collision and passed when rerun alone. All three setup tests were included in that suite. No live provider account or model request was used by these automated checks.

The README now describes source builds, separate account setup, example workflows, companion MCP services and security boundaries. Source builds avoid the default private image registry. These checks publish source changes only; they do not rebuild or deploy the owner's production stack.
