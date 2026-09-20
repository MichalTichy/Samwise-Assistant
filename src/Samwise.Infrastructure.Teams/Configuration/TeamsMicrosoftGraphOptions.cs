namespace Samwise.Infrastructure.Teams.Configuration;

public sealed class TeamsMicrosoftGraphOptions
{
    public const string SectionName = "TeamsInput:MicrosoftGraph";

    public string TenantId { get; set; } = "organizations";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string DataPath { get; set; } = "/app/data-protection/teams";
    public Uri GraphBaseUrl { get; set; } = new("https://graph.microsoft.com/v1.0/");

    public string[] DelegatedScopes { get; set; } =
    [
        "User.Read",
        "Chat.ReadWrite",
        "ChatMessage.Send",
        "Team.ReadBasic.All",
        "Channel.ReadBasic.All",
        "ChannelMessage.Read.All",
        "ChannelMessage.Send",
        "Calendars.ReadWrite"
    ];
}
