# Todoist task source

The module keeps its original project name, configuration document ID and Data Protection purpose so saved credentials and history survive the move. The UI is now `/tasks/sources/todoist`; the former configuration URL is an alias. It does not create Intake events.

Only dated tasks in an explicitly selected project OR with a selected label are delegated. Exclusions override inclusion. Tasks assigned to another account are excluded. No selections means no delegation. The overdue range limits initial imports; future dated tasks appear directly in the calendar. Existing linked tasks are reconciled regardless of age.

`SyncTodoistTasksUsecase` reads task details and recent comments into an internal `AgentTask`. Todoist owns title, instructions and source due date; Samwise owns checklist, evidence and the next check. Identical polls do not change the task version or overwrite the next check. Source changes withdraw unexecuted plans and update the same instance, retaining evidence. A due-date change alone is a postponement. A higher completion counter or an explicitly completed recurring occurrence followed by a later recurring due date creates a fresh occurrence. Restoration of the same occurrence preserves evidence. Missing/regressed counters retain the last known baseline. New generations retain unresolved execution origins; restored access cannot discard an unresolved failure.

Completion, deletion, loss of delegation and removal of a due date stop automatic checks. A 404 means unavailable, not completed. API errors prevent a run without asserting completion. The scheduler and execution service refresh the exact source before starting. There is no automatic writeback to Todoist; any human-action Todoist creation or update still requires the normal approved execution tools.

Legacy inbound events remain readable as history. Pending legacy events are retired without Intake. On first import, one live legacy work item is adopted; multiple live work items produce a visible reconciliation error instead of duplicating work.
