# Samwise.Inputs.Manual

This feature module owns manual input from capture through creation of its concrete `InboundEvent`. `SubmitManualInputUsecase` persists a pending event and returns immediately. Its processing queue wakes a hosted worker, which triggers the generic processing use case in a fresh scope. A periodic pending-event sweep recovers submissions interrupted after persistence or during a restart.

The Web project owns only the input form. No manual-input type belongs in `Samwise.Domain` or the generic application workflow. The existing synchronous capture path remains in place for other input sources.
