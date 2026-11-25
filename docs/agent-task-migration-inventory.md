# Migration inventory

Read-only snapshot of Todoist and all 18 local T3 projects, 2026-11-17. Nothing was imported, disabled, completed or rescheduled. Selection and deployment cutover remain separate.

## Todoist recurring tasks

| Task | Recurrence | Proposed destination |
|---|---|---|
| [Máma svátek](https://app.todoist.com/app/task/6C5FF3pm75Q5G4CH) | Every year | Keep as a human Todoist reminder |
| [Vyfakturovat RIGANTI](https://app.todoist.com/app/task/6RH8h8JJ6p6hW9VW) | every 1st workday | Candidate for Samwise delegated task with checklist |
| [zvážit se](https://app.todoist.com/app/task/6V72f75cx5gwh7R8) | every sun | Keep as a human Todoist reminder |
| [Doplnit dostupnost](https://app.todoist.com/app/task/6WHH7xJqjWM3Cr6W) | every 2nd | Keep as a human Todoist reminder |
| [Vetstor - Faktura](https://app.todoist.com/app/task/6hXmrjHwh37fWR4g) | every 3rd | Candidate for Samwise delegated task with checklist |
| [Notino – fakturace](https://app.todoist.com/app/task/6hgFVWfMPJjpX8pW) | every last workday at 9am | Candidate for Samwise delegated task with checklist |
| [Solvertech Freya – fakturace](https://app.todoist.com/app/task/6hgFX25gg4JQ2mxj) | every 2nd | Candidate for Samwise delegated task with checklist |
| [Education reporting zprávy](https://app.todoist.com/app/task/6hjrHXrJ7cfCccCW) | every 2nd mon | Candidate for Samwise delegated task with checklist |

The Notino and Vetstor parents have undated child tasks. Preserve their instructions/references as checklist phases when cutting over; do not import each child as an unrelated automation. The older AI Brain Notino daily-control link needs reconciliation rather than creating another daily task beside the parent cycle. Select specific labels on delegated parent tasks where a project mixes human and agent work.

Other active Todoist tasks include physical parcel collection, shopping, wedding planning and undated project notes: keep these in Todoist. Dated business candidates (Edifio changes, forgotten Solvertech invoice, meeting scheduling, ComSTAR deployment check) require explicit delegation and current evidence, not a blanket project import.

## T3 schedules

| Project | Task | Enabled | Schedule | Cutover |
|---|---|---|---|---|
| rigantiIs | Freya weekly Michal H report | yes | 09:00; weekdays 1 (0=Sun) | Recurring candidate |
| work-tracking | Complete approved work-tracking sync for 2026-10-27 and 2026-11-03 | yes | 2026-11-17T18:57:00.000Z | Reconcile existing result; do not recreate automatically |
| work-tracking | Complete work tracking 2026-11-04 after Toggl quota reset | yes | 2026-11-05T10:53:00.000Z | Reconcile existing result; do not recreate automatically |
| work-tracking | Complete work tracking 2026-11-02 after Toggl quota reset | no | every 60 min | Reconcile existing result; do not recreate automatically |
| work-tracking | Dokončit work tracking 2026-10-29 po resetu Toggl kvóty | no | every 60 min | Reconcile existing result; do not recreate automatically |
| work-tracking | Finish approved Toggl sync · 23–25 October | no | every 60 min | Reconcile existing result; do not recreate automatically |
| work-tracking | Daily work tracking | yes | 06:00; weekdays 2,3,4,5,6 (0=Sun) | Recurring candidate |
| OPR0123-RSM-Mzdy | Nasadit Linux agenty a znovu spustit selhané CI | no | every 5 min | Reconcile existing result; do not recreate automatically |
| OPR0123-RSM-Mzdy | Zapiš hodiny do Azure DevOps | yes | 09:00; weekdays 0,1,3,5 (0=Sun) | Recurring candidate |
| Samwise-Assistant | Ověřit nasazení Show as a obnovit plán paní Illnerové | no | every 5 min | Reconcile existing result; do not recreate automatically |
| No project | FPV bazar – brýle a tiny whoopy | yes | 20:00; weekdays all (0=Sun) | Recurring candidate |
| BakerHughes | Reflect final additional Baker Hughes prototype changes | no | every 10 min | Reconcile existing result; do not recreate automatically |
| BLUP | BLUP – sledování běhu z 8. 11. 14:22 | no | every 60 min | Reconcile existing result; do not recreate automatically |
| BLUP | BLUP V1 – dokončení a odeslání Honzovi do Teams | no | 2026-11-06T01:46:07.000Z | Reconcile existing result; do not recreate automatically |

Four enabled recurring candidates: daily work tracking (Tue–Sat 06:00), ADO hours (Sun/Mon/Wed/Fri 09:00), Freya weekly report (Mon 09:00), FPV offers (daily 20:00), using Europe/Prague per the existing instructions. Preserve approval boundaries, deduplication history and existing automation memory. The Freya report and work-tracking jobs refer to local files/skills; verify these are reachable from the deployed execution runtime before transfer. FPV requires persistent seen-offer history across independent occurrences.

Two enabled one-off work-tracking retries already contain approved queues and partial-write evidence. Reconcile those exact queues and live entries before any migration; do not regenerate or duplicate their writes. Disabled deployment/prototype/BLUP monitors are historical, not new recurring templates.

For each cutover: prepare the task/checklist and runtime grants, reconcile the current occurrence and successful effects, verify one delegated run, then disable only the corresponding old schedule. Do not run an imported Todoist recurrence and a new native routine for the same cycle.
