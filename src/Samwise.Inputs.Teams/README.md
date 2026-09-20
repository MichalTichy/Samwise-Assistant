# Samwise.Inputs.Teams

This is the complete application module for Microsoft Teams input. It owns Teams configuration, conversation and transcript event documents, typed source locators, reader transport models and port, processing and configuration use cases, conversation specifications, locking, and initialization.

`Samwise.Infrastructure.Teams` is the direct Microsoft Graph adapter and polling host for this module. The UI-managed configuration contains the Entra application registration; the infrastructure adapter protects its optional secret and owns the delegated token cache. The module captures concrete events through the generic `IInboundEventCaptureService`; neither `Samwise.Domain` nor the generic workflow depends on Teams.
