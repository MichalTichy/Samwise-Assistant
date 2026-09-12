# Samwise.Infrastructure.Email.Imap

This project is the inbound-email adapter. It connects enabled accounts to IMAP with MailKit, always opens mailboxes read-only, decrypts stored account credentials through ASP.NET Data Protection, and converts messages into the source-neutral contracts owned by `Samwise.Logic`.

## Main components

- `Mailboxes/MailKitEmailInboxReader` authenticates with a username and password or app password. Automatic reads use the account's durable INBOX UID checkpoint and therefore include newly delivered mail even when it has already been read; manual reads search only unread mail.
- The reader captures read/answered flags, attachment metadata without downloading attachment content, and up to five matching sent replies discovered through `Message-ID`, `In-Reply-To`, and `References` headers. Missing or inaccessible Sent folders do not prevent inbox ingestion.
- `Credentials/DataProtectionEmailCredentialProtector` protects account passwords with the same persisted key ring used by the Samwise web application.
- `Polling/EmailPollingBackgroundService` waits for the UI-configured interval and triggers `PollEmailAccountsUsecase`. The timer is an external use-case trigger; processing and workflow coordination remain in `Samwise.Logic`.
- `Installers/EmailImapInstaller` registers the adapter and the in-process poller.

## Boundaries

The project contains no domain decisions about actionability, ignored senders, work-item matching, or planning. It never changes IMAP read state and does not send email. OAuth and provider-specific integrations are outside V1.
