# Samwise.Infrastructure.Teams

This project connects the Teams input module directly to Microsoft Graph. Samwise owns its delegated device-code sign-in and persistent MSAL cache; transcript discovery optionally uses the configured application credentials. Teams intake has no runtime dependency on the Teams MCP server.

`MicrosoftGraphTeamsInputReader` reads connected people, chats, teams, and channels for configuration pickers and retrieves recent conversation snapshots and transcript metadata. `Samwise.Inputs.Teams` applies the configured quiet period, groups one chat or one channel thread into a conversation-update event, separates previously observed context from new trigger messages, and owns checkpointing and ignore rules. Durable events use explicit chat, channel-thread, scheduled-meeting, and ad-hoc-call source types instead of nullable identifier combinations.

Samwise deliberately does not download transcript content while polling. A transcript input event stores only stable Microsoft identifiers and timing metadata. Intake, planning, or execution agents can retrieve the content explicitly through an available agent tool when they need it.

Transcript discovery combines the application-permission organizer feed with delegated discovery for meetings in the connected account's calendar, including invitations organized by colleagues. The latter resolves each meeting's join URL and lists its transcripts with the connected user's access. It revisits recent transcripts for one day so previously missed attendee meetings can be captured; durable event IDs prevent duplicate processing. The connected account needs delegated `Calendars.Read`, `OnlineMeetings.Read`, and `OnlineMeetingTranscript.Read.All` consent in addition to the existing Teams scopes. Existing connections must sign in again after these scopes are added. Tenant transcript API policy can still restrict access.

Invited-meeting lookup runs with a small concurrency limit and reports access failures per meeting without losing transcripts retrieved from other meetings. Repeated Graph errors are summarized in the processing result so a single inaccessible meeting does not hide successful discoveries.

The Web UI owns the Entra tenant ID, client ID, protected optional client secret, and delegated account connection. A client secret is required only for transcript discovery. Runtime-only Graph endpoint and token-cache path settings stay in infrastructure configuration. The optional Teams MCP service remains a Gateway tool provider for planning and execution, not an intake dependency.
