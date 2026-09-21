# Samwise.Inputs.Manual

This feature module owns manual input from capture through creation of its concrete `InboundEvent`. `SubmitManualInputUsecase` persists a pending event and returns immediately. The shared inbound-event queue wakes the Web-hosted worker, which triggers the generic processing use case in a fresh scope. A periodic pending-event sweep recovers submissions interrupted after persistence or during a restart.

The Web project owns the input form and the shared processing worker. No manual-input type belongs in `Samwise.Domain` or the generic application workflow. Email, Teams, and Todoist use the same capture-and-queue path.
