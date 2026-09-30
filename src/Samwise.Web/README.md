# Samwise.Web

This is the single deployable Samwise process and the Blazor Interactive Server presentation layer. It composes all installers and initializers, owns authentication-facing endpoints, and presents the manual-input, inbox, board, work-item, agent-settings, playbook, email-input, and account experiences. The board shows active and completed Work Items; discarded Work Items stay out of its Done column.

## Structure

- `Program.cs` is only the host composition and HTTP pipeline entry point, including the first-party `/mcp` route.
- `Installers/WebInstaller` registers Razor components, LumexUI, and server authentication state.
- `Components/App.razor` defines the HTML document, generated stylesheets, LumexUI script, and Blazor boot script.
- `Components/Routes.razor` applies authorization and redirects anonymous users to login.
- `Components/Layout/` contains the authenticated application shell, navigation, reconnect UI, and the shell-free authentication layout.
- `Components/Pages/` contains each product page as a `.razor` markup file plus `.razor.cs` behavior file.
- `Components/Pages/Login` and `Components/Pages/ChangePassword` are the deliberately small custom account UI.
- `Components/Shared/` contains presentation-only helpers such as login redirection.
- `Endpoints/OwnerAccountEndpoints` contains the antiforgery-protected logout endpoint.
- `Styles/app.css` is the Tailwind/LumexUI source; `wwwroot/app.css` is the generated asset.
- `appsettings*.json` contains safe local defaults and option structure; secrets belong in user secrets or environment variables.

## Read and command boundary

- Pages inject `IReadOnlyRepository<T>` and execute public specifications from `Samwise.Logic` directly for reads.
- Pages send MediatR use cases only for commands or processing initiated by the user.
- Pages do not use Marten sessions or infrastructure document APIs.
- Page-specific mapping and display state stay in the code-behind.

## Composition flow

1. Service defaults are registered.
2. Installer discovery composes logic, persistence, the standard OpenAI runtime, Gateway catalog extensions, the first-party MCP adapter, Identity, and web services.
3. Initializers migrate Identity, ensure the owner, and seed the complete agent-configuration document.
4. Authentication, authorization, antiforgery, static assets, health endpoints, Razor components, logout, and the API-key-protected MCP endpoint are mapped.
5. Startup runs before the server accepts normal use, and application-ready initializers run after the server starts.

## Authentication flow

1. The fallback authorization policy protects every route unless it explicitly allows anonymous access.
2. The custom login page uses ASP.NET Core Identity to issue the application cookie.
3. The login page uses `AuthLayout`, so application navigation is never rendered before authentication.
4. `AuthorizeRouteView` renders authenticated pages or redirects to `/login`.
5. Password change and logout use only the custom UI and endpoint needed by the single owner.

## Page convention

Custom pages keep markup in `Page.razor` and behavior in `Page.razor.cs`. UI code may depend on application contracts and shared repository abstractions, but it must not use Marten sessions directly.

## Manual-input flow

1. `Home` validates the user's text and sends `SubmitManualInputUsecase`. `POST /api/manual-input` accepts the same input from mobile automation without opening a confirmation screen. It requires its own write-only API key and never exposes that key in the URL.
2. The use case persists a pending event and queues background processing without waiting for AI.
3. The page navigates to the inbox audit view immediately. The inbox refreshes while pending events remain.
4. `Inbox` reads `InboundEvent` documents with `InboxEventsSpecification` and can send a retry command for events needing review.

The Web-hosted `InboundEventProcessingBackgroundService` handles the shared queue for every input source. It runs Intake and Planning one event at a time, then periodically requeues persisted pending events so a restart cannot lose them. Connector checks and manual submission return after capture, not after AI processing.

## Work-item flow

1. `Board` reads work items through `BoardWorkItemsSpecification` and maps them into UI cards, including a separate Needs your input column for planning questions. `DiscardedWorkItems` uses its own specification to keep discarded records out of the active Board while making their discard reason, detail, and Restore & replan action discoverable from desktop and mobile navigation.
2. `WorkItemDetails` loads the aggregate first, then agent invocations through `WorkItemInvocationsSpecification`. While open, it checks for aggregate changes and reloads after background planning or execution changes the status.
3. Selecting an agent-activity row opens its full invocation record in a Lumex dialog, including the prompt, output, model metadata, error, and available-tool snapshot.
4. Suggested tool arguments are rendered as labeled nested values, lists, booleans, and readable multiline content rather than requiring the user to decode JSON. Invalid historical argument payloads fall back to escaped plain text.
5. When planning needs a material user preference, the detail presents two to six suggested choices plus Other. Submitting one stores the decision and immediately replans; the resulting plan displays the decisions it used.
6. The detail loads its linked input events and shows their original content in a source-specific, human-readable card beside the plan on wide screens. Teams shows the triggering messages and destination chat/channel; email shows sender, recipients, subject, and body. Samwise's Work Item description is separate from the source text.
7. Approve, reject, add-context, replan, retry-execution, discard, and restore buttons send their corresponding external use cases. Replan is available for a pending planning question, a plan awaiting approval, or a planning/execution failure; it keeps prior plans and agent activity in history. Restore is available only for discarded Work Items and starts a new planning revision. Retry execution is available only for `OutcomeUnknown`, retains the uncertain invocation, and warns that the same approved plan can repeat external changes.
7. Each command reloads the authoritative stored state after it completes.

## Agent settings and playbook flow

1. `AgentSettings` reads the single `AgentConfiguration` through `IReadOnlyRepository<AgentConfiguration>`, expands its three nested stages for display, and obtains each stage's placeholder legend from `IAgentPromptPlaceholderCatalog`.
2. It loads the Gateway model and MCP tool catalogs separately for each stage. The selected stage shows its granted MCP servers and grouped tools without exposing the profile credential to the browser.
3. It presents model and model-specific reasoning-effort dropdowns and selects the Gateway-reported default when the model changes.
4. Saving sends `SaveAgentStageConfigurationUsecase`, which validates the model/effort pair and prompt before updating the selected nested stage in that document.
5. `Playbooks` reads playbook documents directly through `PlaybooksSpecification`; save actions send `SavePlaybookUsecase`.
6. Both pages reload the saved documents rather than treating browser state as authoritative.

## Email-input flow

1. `EmailInputs` lives at Configuration → Inputs → Emails and reads the singleton `EmailInputConfiguration` plus individual `EmailAccount` documents through repositories.
2. Saving the interval or exact-address ignore list sends `SaveEmailInputConfigurationUsecase`. Each account also has trusted intake guidance captured with its future events. Accounts may use a protected password or Microsoft 365 OAuth. Microsoft sign-in starts through `StartMicrosoftEmailAuthorizationUsecase`; saving consumes that authorization through `SaveEmailAccountUsecase`, verifies INBOX access, and establishes the current INBOX and Sent UIDs as automatic-import starting points.
3. Connection-test and per-account/global process buttons trigger their external use cases and display the returned outcome.
4. Automatic processing is always active for enabled accounts. The timer triggers `ProcessEmailsUsecase(NewSinceCheckpoint)` to import new received and sent messages; the manual buttons trigger `ProcessEmailsUsecase(Unread)` and may import older unread INBOX messages only.

## Teams-input flow

1. `TeamsInputs` lives at Configuration → Inputs → Microsoft Teams and edits the singleton `TeamsInputConfiguration`, including polling, conversation debounce, source guidance, and enabled activity kinds.
2. The page configures the Entra application, protects its optional client secret, owns Microsoft device-code sign-in, and loads searchable people, chat, and channel options directly from Microsoft Graph. The reusable `TeamsSourcePicker` handles each ignored list.
3. Users can enable chat messages, channel messages, and transcript metadata independently. Transcript content is never downloaded by polling.
4. Automatic polling and the Process now action both send `ProcessTeamsInputsUsecase`. Messages in one chat or channel thread wait for the configured quiet period and become one conversation-update event. Previously processed messages are labelled context and linked to their earlier event outcomes. The shared inbound pipeline deduplicates resulting events and establishes a current-time baseline on first use.
5. Account passwords are never returned to the page, and editing with a blank password keeps the protected value already stored.

## Frontend asset flow

1. Tailwind scans `Styles/app.css` and component markup using the sources configured in the stylesheet.
2. `npm run build:css` generates the deployable `wwwroot/app.css` file.
3. The generated stylesheet is committed so ordinary Debug builds do not require Node.js.
4. Release builds run the frontend build target and regenerate the stylesheet.
5. Change the source stylesheet or component classes, then regenerate; do not hand-edit `wwwroot/app.css`.

## UI principles

The UI is a focused personal tool, not an enterprise account portal. Use LumexUI and the Samwise palette; avoid Bootstrap, generated Identity scaffolding, registration, recovery, 2FA, and passkey flows unless a concrete product need is added.
