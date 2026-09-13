# Samwise.Infrastructure.Email.Microsoft365

This project is the Microsoft 365 authentication adapter for inbound email. It extends the shared MailKit IMAP transport with Microsoft device-code authorization, encrypted MSAL token-cache persistence, silent token refresh, and XOAUTH2 authentication.

## Main components

- `Installers/Microsoft365EmailInstaller` registers Microsoft configuration, device-code authorization, token acquisition, and the Microsoft 365 IMAP authentication handler.
- `MicrosoftEmailAuthorizationService` implements the user-triggered device-code flow used by `StartMicrosoftEmailAuthorizationUsecase` and completed while saving an account.
- `Authentication/MicrosoftEmailAccessTokenProvider` restores and silently refreshes the encrypted per-account MSAL cache.
- `Authentication/Microsoft365EmailAuthenticationHandler` supplies XOAUTH2 authentication to the generic reader in `Samwise.Infrastructure.Email.Imap`.

## Boundaries

The project owns only Microsoft 365 identity behavior. It does not read mailboxes, schedule polling, capture inbound events, or decide what an email means. It requests delegated `IMAP.AccessAsUser.All` access only and requires a public-client Entra application configured under `Email:Microsoft365`.
