# Todoist task source

The application module owns synchronization of external calendar tasks; the API adapter and polling host remain in `Samwise.Infrastructure.Todoist`. Configuration, discovery, synchronization and source run gates live here. Shared due-date models live in `Models`; the old inbound event types live in `Legacy` only for historical reads and migration reconciliation.

The configuration document ID, table alias and Data Protection purpose remain stable. A startup migration rewrites stored CLR type metadata so saved credentials and history survive the project and namespace move. The UI is now `/tasks/sources/todoist`; the former configuration URL is an alias. It does not create Intake events.

All dated tasks assigned to the owner or nobody are imported, including older overdue tasks. Explicit project/label exclusions remain effective. Projects and labels are context rather than a delegation gate. Future dated tasks appear directly in the calendar. Planning assesses useful action when a task is due; a human task with nothing useful for Samwise remains open after the assessment ends.

`SyncTodoistTasksUsecase` reads task details and recent comments into an internal `AgentTask`. Todoist owns title, instructions and source due date; Samwise owns checklist, evidence and the next check. Identical polls do not change the task version or overwrite the next check. Source changes withdraw unexecuted plans and update the same instance, retaining evidence. A due-date change alone is a postponement. A higher completion counter or an explicitly completed recurring occurrence followed by a later recurring due date creates a fresh occurrence. Restoration of the same occurrence preserves evidence. Missing/regressed counters retain the last known baseline. New generations retain unresolved execution origins; restored access cannot discard an unresolved failure.

Completion, deletion, explicit exclusion and removal of a due date stop automatic checks. A 404 means unavailable, not completed. API errors prevent a run without asserting completion. The scheduler and execution service refresh the exact source before starting. There is no automatic writeback to Todoist; any human-action Todoist creation or update still requires the normal approved execution tools.

Legacy inbound events remain readable as history. Pending legacy events are retired without Intake. On first import, one live legacy work item is adopted; multiple live work items produce a visible reconciliation error instead of duplicating work.
