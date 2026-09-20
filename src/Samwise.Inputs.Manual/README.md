# Samwise.Inputs.Manual

This feature module owns manual input from capture through creation of its concrete `InboundEvent`. It contains the externally triggered submit use case and the persisted manual-event document. The module delegates generic classification and Work Item processing to `Samwise.Logic` through the shared inbound-event capture port.

The Web project owns only the input form. No manual-input type belongs in `Samwise.Domain` or the generic application workflow.
