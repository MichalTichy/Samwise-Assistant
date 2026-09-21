using Samwise.Infrastructure.Teams.Readers;

namespace Samwise.Tests.Inputs;

public sealed class TeamsGraphReaderTests
{
    [Fact]
    public void RecentChatsUseGraphSupportedOrdering()
    {
        Assert.Equal(
            "me/chats?$top=50&$orderby=lastMessagePreview/createdDateTime desc",
            MicrosoftGraphTeamsInputReader.RecentChatsPath);
    }
}
