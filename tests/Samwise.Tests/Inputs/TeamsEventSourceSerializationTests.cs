using Newtonsoft.Json;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Teams.InboundEvents.Conversations;
using Samwise.Inputs.Teams.InboundEvents.Transcripts;

namespace Samwise.Tests.Inputs;

public sealed class TeamsEventSourceSerializationTests
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.Objects
    };

    [Fact]
    public void ConversationSourceRetainsItsConcreteType()
    {
        var inboundEvent = new TeamsConversationInboundEvent
        {
            ConversationVersion = "version-1",
            Conversation = new TeamsChannelThreadConversationSource
            {
                TeamId = "team-1",
                TeamName = "The Fellowship",
                ChannelId = "channel-1",
                ChannelName = "General",
                ThreadRootMessageId = "root-1"
            }
        };

        var json = JsonConvert.SerializeObject(inboundEvent, Settings);
        var roundTrip = JsonConvert.DeserializeObject<TeamsConversationInboundEvent>(json, Settings);

        var source = Assert.IsType<TeamsChannelThreadConversationSource>(roundTrip?.Conversation);
        Assert.Equal("team-1", source.TeamId);
        Assert.Equal("root-1", source.ThreadRootMessageId);
    }

    [Fact]
    public void TranscriptSourceRetainsItsConcreteType()
    {
        var inboundEvent = new TeamsTranscriptInboundEvent
        {
            TranscriptId = "transcript-1",
            TranscriptSource = new TeamsScheduledMeetingTranscriptSource
            {
                MeetingId = "meeting-1"
            }
        };

        var json = JsonConvert.SerializeObject(inboundEvent, Settings);
        var roundTrip = JsonConvert.DeserializeObject<TeamsTranscriptInboundEvent>(json, Settings);

        var source = Assert.IsType<TeamsScheduledMeetingTranscriptSource>(roundTrip?.TranscriptSource);
        Assert.Equal("meeting-1", source.MeetingId);
    }
}
