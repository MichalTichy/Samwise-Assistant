# Samwise.Infrastructure.Persistence.Identity

This project implements the deliberately small single-owner authentication system. It uses ASP.NET Core Identity for password hashing, validation, cookies, and password changes without scaffolded registration or account-management features.

## Main components

- `ApplicationUser` is the one supported Identity user type.
- `ApplicationDbContext` stores Identity tables in PostgreSQL's `identity` schema.
- `IdentityInstaller` configures Identity Core, cookie authentication, fallback authorization, EF Core, durable Data Protection keys, and owner settings. Production cookies require HTTPS by default; `Authentication:RequireHttps=false` is the explicit escape hatch for a direct-HTTP trusted-network deployment.
- `OwnerAccountOptions` binds the configured email and first-start password.
- `OwnerIdentityInitializer` runs EF migrations and creates or validates the single owner.
- `Migrations/` contains the checked-in Identity schema migration.

## First-start flow

1. Installer discovery configures Identity and the application cookie.
2. During the `OnStartup` initialization phase, `OwnerIdentityInitializer` migrates the Identity schema.
3. If no user exists, it creates the configured owner with `OwnerAccount:InitialPassword`.
4. If one owner exists, startup validates the configured email and never resets an existing password.
5. More than one stored user fails startup because it violates the product boundary.

## Request flow

1. The application-wide fallback policy requires authentication unless an endpoint explicitly allows anonymous access.
2. The custom login page uses `SignInManager` to issue the application cookie.
3. Durable Data Protection keys keep that cookie valid across restarts.
4. The custom change-password page updates the one owner's password through Identity APIs.

## Deliberate omissions

V1 has no registration, email sending, email confirmation, password recovery, external login, roles, 2FA, passkeys, lockout workflow, or generated Identity UI. Add only a concrete personal-assistant need, not the full Identity scaffold.
