# Samwise.Infrastructure.Email.Imap

This project is the provider-neutral inbound-email adapter. It connects enabled accounts to IMAP with MailKit, always opens mailboxes read-only, delegates authentication to installed handlers, and converts messages into the source-neutral contracts owned by `Samwise.Logic`.

## Main components

- `Mailboxes/MailKitEmailInboxReader` owns IMAP connection and read behavior, then selects the `IEmailAuthenticationHandler` matching the account. Automatic reads use the account's durable INBOX UID checkpoint and therefore include newly delivered mail even when it has already been read; manual reads search only unread mail.
- The reader captures read/answered flags, attachment metadata without downloading attachment content, and up to five matching sent replies discovered through `Message-ID`, `In-Reply-To`, and `References` headers. Missing or inaccessible Sent folders do not prevent inbox ingestion.
- `Credentials/DataProtectionEmailCredentialProtector` protects account passwords with the same persisted key ring used by the Samwise web application.
- `Authentication/PasswordEmailAuthenticationHandler` provides generic username/password and app-password authentication. Microsoft 365 OAuth lives in the separate `Samwise.Infrastructure.Email.Microsoft365` project.
- `Polling/EmailPollingBackgroundService` waits for the UI-configured interval and triggers `ProcessEmailsUsecase` with `NewSinceCheckpoint`. The timer is an external use-case trigger; processing and workflow coordination remain in the use-case handler in `Samwise.Logic`.
- `Installers/EmailImapInstaller` registers the adapter and the in-process poller.

## Boundaries

The project contains no domain decisions about actionability, ignored senders, work-item matching, or planning. It never changes IMAP read state and does not send email. Provider-specific identity dependencies do not belong here; mail sending, calendar, and contacts remain MCP operations.
