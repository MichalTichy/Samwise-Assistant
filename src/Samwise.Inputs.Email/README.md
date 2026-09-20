# Samwise.Inputs.Email

This is the complete application module for email input. It owns email account and polling configuration documents, received- and sent-email event documents, source snapshots, reader and authorization ports, processing and configuration use cases, specifications, locking, and initialization.

Protocol adapters remain separate: `Samwise.Infrastructure.Email.Imap` implements mailbox access and polling, while `Samwise.Infrastructure.Email.Microsoft365` implements Microsoft authorization. Both depend on this module. The module hands captured events to the generic intake pipeline in `Samwise.Logic`; generic application code does not depend on email types.
