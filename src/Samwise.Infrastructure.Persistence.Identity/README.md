# Samwise.Infrastructure.Persistence.Identity

This project implements the deliberately small single-owner authentication system. It uses ASP.NET Core Identity for password hashing, validation, cookies, and password changes with a minimal one-time owner registration page instead of a public registration system.

## Main components

- `ApplicationUser` is the one supported Identity user type.
- `ApplicationDbContext` stores Identity tables in PostgreSQL's `identity` schema.
- `IdentityInstaller` configures Identity Core, cookie authentication, fallback authorization, EF Core, durable Data Protection keys, and owner settings. Cookies follow the request scheme by default, so HTTPS receives a secure cookie while direct HTTP remains usable on a trusted network. `Authentication:RequireHttps=true` enforces HTTPS-only cookies.
- `OwnerAccountOptions` binds a private first-owner registration token and optional legacy email/initial password.
- `OwnerAccounts/` contains the registration use case, owner read specification, token access checks and post-commit creation notification. The handler and legacy initializer share a PostgreSQL advisory transaction lock to prevent concurrent creation of two owners.
- `OwnerIdentityInitializer` runs EF migrations and creates or validates the single owner.
- `Migrations/` contains the checked-in Identity schema migration.

## First-start flow

1. Installer discovery configures Identity and the application cookie.
2. During the `OnStartup` initialization phase, `OwnerIdentityInitializer` migrates the Identity schema.
3. If no user exists and a registration token of at least 32 characters is configured, startup leaves account creation to `/register?token=...`. The owner enters email/password in the browser; after the first account exists registration is unavailable, including after restarts. Without a token, explicit legacy email/initial-password provisioning is supported.
4. If one owner exists, startup validates the configured email and never resets an existing password.
5. More than one stored user fails startup because it violates the product boundary.

## Request flow

1. The application-wide fallback policy requires authentication unless an endpoint explicitly allows anonymous access.
2. The custom login page reads the stored owner identity and uses `SignInManager` to issue the application cookie; it does not require copying a registered email back into configuration.
3. Durable Data Protection keys keep that cookie valid across restarts.
4. The custom change-password page updates the one owner's password through Identity APIs.

## Deliberate omissions

There is no public registration, email sending, email confirmation, password recovery, external login, roles, 2FA, passkeys, lockout workflow, or generated Identity UI. Add only a concrete personal-assistant need, not the full Identity scaffold.

## Registration verification

Run Identity registration integration tests against disposable PostgreSQL by setting `SAMWISE_IDENTITY_TEST_CONNECTION` to a connection string with database creation permission, then run `dotnet test`. Each test creates and drops its own database. Without that setting those PostgreSQL integration tests are explicitly skipped; token unit tests still run.
