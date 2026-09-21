# Samwise.Inputs.Teams

This is the complete application module for Microsoft Teams input. It owns Teams configuration, conversation and transcript event documents, typed source locators, reader transport models and port, processing and configuration use cases, conversation specifications, locking, and initialization.

`Samwise.Infrastructure.Teams` is the direct Microsoft Graph adapter and polling host for this module. The UI-managed configuration contains the Entra application registration; the infrastructure adapter protects its optional secret and owns the delegated token cache. The module captures concrete events through the generic `IInboundEventCaptureService`; neither `Samwise.Domain` nor the generic workflow depends on Teams.

## Structure

- `Authentication` contains the account-connection port, device-code state, and credential-protection port.
- `Configuration` contains the persisted input and Graph-application configuration.
- `Discovery` contains the people, chat, and channel catalogue exposed to configuration UI.
- `Ingestion` contains the reader port, processing result, and per-process lock. `Ingestion/Activities` models chat messages, channel messages, scheduled-meeting transcripts, and ad-hoc-call transcripts as separate types instead of one nullable property bag.
- `InboundEvents/Conversations` contains conversation-update documents, messages, typed chat/channel sources, and their specification.
- `InboundEvents/Transcripts` contains transcript documents and typed scheduled-meeting/ad-hoc-call sources.
- `Usecases/Authorization`, `Usecases/Configuration`, and `Usecases/Processing` contain only externally triggered application operations.
- `Initialization` and `Installers` compose the module without leaking Teams types into the generic Domain or Logic projects.
