# Samwise.Web

This is the single deployable Samwise process and the Blazor Interactive Server presentation layer. It composes all installers and initializers, owns authentication-facing endpoints, and provides the authenticated LumexUI application shell.

## Structure

- `Program.cs` is only the host composition and HTTP pipeline entry point.
- `Installers/WebInstaller` registers Razor components, LumexUI, and server authentication state.
- `Components/App.razor` defines the HTML document, generated stylesheets, LumexUI script, and Blazor boot script.
- `Components/Routes.razor` applies authorization and redirects anonymous users to login.
- `Components/Layout/` contains the authenticated application shell, navigation, and reconnect UI.
- `Components/Pages/Login` and `Components/Pages/ChangePassword` are the deliberately small custom account UI.
- `Components/Shared/` contains presentation-only helpers such as login redirection.
- `Endpoints/OwnerAccountEndpoints` contains the antiforgery-protected logout endpoint.
- `Styles/app.css` is the Tailwind/LumexUI source, and the package scripts define stylesheet generation.
- `appsettings*.json` contains safe local defaults and option structure; secrets belong in user secrets or environment variables.

## Composition flow

1. Service defaults are registered.
2. Installer discovery composes logic, persistence, the standard OpenAI runtime, Gateway catalog extensions, Identity, and web services.
3. Initializers migrate Identity, ensure the owner, and seed the complete agent-configuration document.
4. Authentication, authorization, antiforgery, static assets, health endpoints, Razor components, and the logout endpoint are mapped.
5. Startup runs before the server accepts normal use, and application-ready initializers run after the server starts.

## Authentication flow

1. The fallback authorization policy protects every route unless it explicitly allows anonymous access.
2. The custom login page uses ASP.NET Core Identity to issue the application cookie.
3. `AuthorizeRouteView` renders authenticated pages or redirects to `/login`.
4. Password change and logout use only the custom UI and endpoint needed by the single owner.

## Page convention

Custom pages keep markup in `Page.razor` and behavior in `Page.razor.cs`. UI code may depend on application contracts and shared repository abstractions, but it must not use Marten sessions directly.

## UI principles

The UI is a focused personal tool, not an enterprise account portal. Use LumexUI and the Samwise palette; avoid Bootstrap, generated Identity scaffolding, registration, recovery, 2FA, and passkey flows unless a concrete product need is added.
