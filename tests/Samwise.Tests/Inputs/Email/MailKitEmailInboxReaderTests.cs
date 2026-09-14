using Samwise.Infrastructure.Email.Imap.Mailboxes;

namespace Samwise.Tests.Inputs.Email;

public sealed class MailKitEmailInboxReaderTests
{
    [Theory]
    [InlineData(0, 2, "UID 1:1")]
    [InlineData(42, 45, "UID 42:44")]
    public void NewMessageSearchIsBoundedByInboxSnapshot(
        uint nextUid,
        uint inboxNextUid,
        string expected)
    {
        var query = MailKitEmailInboxReader.CreateNewMessagesSearchQuery(nextUid, inboxNextUid);

        Assert.Equal(expected, query);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(45, 45)]
    [InlineData(46, 45)]
    public void NewMessageSearchIsSkippedWhenSnapshotHasNoNewUid(
        uint nextUid,
        uint inboxNextUid)
    {
        var query = MailKitEmailInboxReader.CreateNewMessagesSearchQuery(nextUid, inboxNextUid);

        Assert.Null(query);
    }

    [Fact]
    public void UnreadSearchUsesPlainImapCriterion()
    {
        Assert.Equal("UNSEEN", MailKitEmailInboxReader.UnreadSearchQuery);
    }

    [Theory]
    [InlineData("Sent")]
    [InlineData("Sent Items")]
    [InlineData("Sent Mail")]
    [InlineData("Odeslaná pošta")]
    [InlineData("Odeslané zprávy")]
    [InlineData("Gesendet")]
    public void CommonSentFolderNamesAreRecognized(string folderName)
    {
        Assert.True(MailKitEmailInboxReader.IsSentFolderName(folderName));
    }

    [Theory]
    [InlineData("Outbox")]
    [InlineData("Drafts")]
    [InlineData("Spam")]
    public void OtherMailboxNamesAreNotTreatedAsSent(string folderName)
    {
        Assert.False(MailKitEmailInboxReader.IsSentFolderName(folderName));
    }
}
