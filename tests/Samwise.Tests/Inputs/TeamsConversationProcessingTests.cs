using Samwise.Domain.Inbox;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Teams.Discovery;
using Samwise.Inputs.Teams.Ingestion;
using Samwise.Inputs.Teams.Ingestion.Activities;
using Samwise.Logic.Inbox.Services;
using Samwise.Inputs.Teams.InboundEvents.Conversations;
using Samwise.Inputs.Teams.InboundEvents.Transcripts;
using Samwise.Inputs.Teams.Usecases.Processing;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Inputs;

public sealed class TeamsConversationProcessingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnconfiguredTeamsInputRemainsInactive()
    {
        var configuration = new TeamsInputConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [Message("message", Now.AddMinutes(-3), isNew: true, "Do not process this yet")],
            capture);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Errors);
        Assert.Empty(capture.Events);
        Assert.Null(configuration.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task MessagesFromOneChatBecomeOneConversationEventWithOlderContext()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [
                Message("old", Now.AddMinutes(-20), isNew: false, "Original request"),
                Message("new-1", Now.AddMinutes(-4), isNew: true, "First update"),
                Message("new-2", Now.AddMinutes(-3), isNew: true, "Second update")
            ],
            capture);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        var inboundEvent = Assert.IsType<TeamsConversationInboundEvent>(Assert.Single(capture.Events));
        var source = Assert.IsType<TeamsChatConversationSource>(inboundEvent.Conversation);
        Assert.Equal("chat-1", source.ChatId);
        Assert.Equal("Rosie", source.ChatName);
        Assert.Equal(["new-1", "new-2"], inboundEvent.Messages.Select(message => message.MessageId));
        var context = Assert.Single(inboundEvent.Context);
        Assert.Equal("old", context.SourceItemId);
        Assert.False(context.PreviouslyProcessed);
        Assert.Equal("Work Teams guidance.", inboundEvent.SourceGuidance);
        Assert.Equal("Michal Tichy", inboundEvent.ConnectedAccount?.DisplayName);
        Assert.All(inboundEvent.Messages, message => Assert.False(message.IsFromConnectedAccount));
        Assert.Contains("Connected account (you): Michal Tichy <michal@example.com>", inboundEvent.GetIntakeText());
        Assert.Equal(Now.AddMinutes(-2), configuration.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task MessagesFromConnectedAccountAreExplicitlyRenderedAsYou()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [Message(
                "mine",
                Now.AddMinutes(-3),
                isNew: true,
                "Can you send me the document?",
                senderId: "connected-user",
                senderName: "Michal Tichy")],
            capture);

        await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TeamsConversationInboundEvent>(Assert.Single(capture.Events));
        Assert.True(Assert.Single(inboundEvent.Messages).IsFromConnectedAccount);
        Assert.Contains("You (Michal Tichy) (message mine)", inboundEvent.GetIntakeText());
    }

    [Fact]
    public async Task ProcessingStopsWhenConnectedAccountIdentityHasNotBeenCaptured()
    {
        var configuration = CreateConfiguration();
        configuration.ClearConnectedAccount();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [Message("message", Now.AddMinutes(-3), isNew: true, "Do not guess the conversation roles")],
            capture);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Empty(capture.Events);
        Assert.Single(result.Errors);
        Assert.Contains("identity is unavailable", result.Errors[0], StringComparison.Ordinal);
        Assert.Equal(result.Errors[0], configuration.LastError);
    }

    [Fact]
    public async Task RepliedToMessageIsKeptWhenConversationContextIsCapped()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var olderMessages = Enumerable.Range(1, 40)
            .Select(index => Message(
                index == 1 ? "root" : $"context-{index}",
                Now.AddMinutes(-100 + index),
                isNew: false,
                index == 1 ? "Original decision request" : $"Context {index}"))
            .Cast<TeamsActivitySnapshot>()
            .ToList();
        olderMessages.Add(Message(
            "answer",
            Now.AddMinutes(-3),
            isNew: true,
            "Yes, do it.",
            replyToMessageId: "root"));
        var handler = CreateHandler(configuration, [], olderMessages, capture);

        await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TeamsConversationInboundEvent>(Assert.Single(capture.Events));
        Assert.Contains(inboundEvent.Context, item => item.SourceItemId == "root");
        Assert.True(inboundEvent.Context.Count <= 30);
    }

    [Fact]
    public async Task OverlappingPreviouslyProcessedMessageBecomesLinkedContextNotANewTrigger()
    {
        var configuration = CreateConfiguration();
        var previous = new TeamsConversationInboundEvent
        {
            Id = "previous-event",
            ContextKey = "teams:chat:chat-1",
            ConversationVersion = "previous-version",
            Conversation = new TeamsChatConversationSource
            {
                ChatId = "chat-1",
                ChatName = "Rosie"
            },
            Messages =
            [
                new TeamsConversationMessage
                {
                    MessageId = "overlap",
                    Version = "v1",
                    SentAt = Now.AddMinutes(-4),
                    BodyText = "Please prepare it."
                }
            ],
            Outcomes =
            [
                new InboundEventOutcome
                {
                    Id = "outcome-1",
                    WorkItemId = "work-item-1",
                    Kind = InboundEventOutcomeKind.CreatedWorkItem
                }
            ]
        };
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [previous],
            [
                Message("overlap", Now.AddMinutes(-4), isNew: true, "Please prepare it."),
                Message("confirmation", Now.AddMinutes(-3), isNew: true, "Yes, do it.")
            ],
            capture);

        await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TeamsConversationInboundEvent>(Assert.Single(capture.Events));
        Assert.Equal("confirmation", Assert.Single(inboundEvent.Messages).MessageId);
        var context = Assert.Single(inboundEvent.Context);
        Assert.True(context.PreviouslyProcessed);
        Assert.Equal(previous.Id, context.InboundEventId);
        Assert.Equal("work-item-1", Assert.Single(context.WorkItemIds));
    }

    [Fact]
    public async Task ConversationWaitsUntilTheConfiguredQuietPeriodHasElapsed()
    {
        var configuration = CreateConfiguration();
        var originalCheckpoint = configuration.LastSuccessfulCheckAt;
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [Message("too-recent", Now.AddMinutes(-1), isNew: true, "One more thought")],
            capture);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(capture.Events);
        Assert.Equal(originalCheckpoint, configuration.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task SkippedMeetingDoesNotHoldBackTeamsCheckpoint()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var handler = CreateHandler(
            configuration,
            [],
            [Message("new", Now.AddMinutes(-3), isNew: true, "Please review this")],
            capture,
            skippedMeetings: ["1 meeting(s): 3004: Meeting properties are not found"]);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);
        Assert.Single(result.SkippedMeetings);
        Assert.Equal(Now.AddMinutes(-2), configuration.LastSuccessfulCheckAt);
        Assert.Null(configuration.LastError);
    }

    [Fact]
    public async Task TenantWideFailureKeepsTeamsCheckpointForRetry()
    {
        var configuration = CreateConfiguration();
        var originalCheckpoint = configuration.LastSuccessfulCheckAt;
        var handler = CreateHandler(
            configuration,
            [],
            [],
            new RecordingCaptureService(),
            errors: ["Graph API access to transcripts is disabled for this tenant."]);

        var result = await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        Assert.Single(result.Errors);
        Assert.Equal(originalCheckpoint, configuration.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task ChannelMessageUsesAChannelThreadSource()
    {
        var configuration = CreateConfiguration();
        var capture = new RecordingCaptureService();
        var activity = new TeamsChannelMessageSnapshot
        {
            ExternalId = "channel:team-1:channel-1:root-1:message-1:v1",
            OccurredAt = Now.AddMinutes(-3),
            BodyText = "Please take a look.",
            SenderId = "person-1",
            SenderName = "Rosie",
            TeamId = "team-1",
            TeamName = "The Fellowship",
            ChannelId = "channel-1",
            ChannelName = "General",
            ConversationName = "The Fellowship / General",
            MessageId = "message-1",
            ReplyToMessageId = "root-1",
            Version = "v1",
            IsNew = true
        };
        var handler = CreateHandler(configuration, [], [activity], capture);

        await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TeamsConversationInboundEvent>(Assert.Single(capture.Events));
        var source = Assert.IsType<TeamsChannelThreadConversationSource>(inboundEvent.Conversation);
        Assert.Equal("team-1", source.TeamId);
        Assert.Equal("channel-1", source.ChannelId);
        Assert.Equal("root-1", source.ThreadRootMessageId);
        Assert.Equal("teams:channel:team-1:channel-1:thread:root-1", inboundEvent.ContextKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TranscriptUsesItsConcreteSource(bool isMeeting)
    {
        var configuration = CreateConfiguration();
        configuration.Update(5, 2, "Work Teams guidance.", true, true, true, [], [], []);
        var capture = new RecordingCaptureService();
        TeamsTranscriptSnapshot activity = isMeeting
            ? new TeamsScheduledMeetingTranscriptSnapshot
            {
                ExternalId = "transcript:scheduledMeeting:meeting-1:transcript-1",
                OccurredAt = Now.AddMinutes(-3),
                SenderId = "organizer-1",
                SenderName = "Gandalf",
                TranscriptId = "transcript-1",
                MeetingId = "meeting-1"
            }
            : new TeamsAdhocCallTranscriptSnapshot
            {
                ExternalId = "transcript:adhocCall:call-1:transcript-1",
                OccurredAt = Now.AddMinutes(-3),
                SenderId = "organizer-1",
                SenderName = "Gandalf",
                TranscriptId = "transcript-1",
                CallId = "call-1"
            };
        var handler = CreateHandler(configuration, [], [activity], capture);

        await handler.Handle(new ProcessTeamsInputsUsecase(), CancellationToken.None);

        var inboundEvent = Assert.IsType<TeamsTranscriptInboundEvent>(Assert.Single(capture.Events));
        if (isMeeting)
        {
            Assert.Equal(
                "meeting-1",
                Assert.IsType<TeamsScheduledMeetingTranscriptSource>(inboundEvent.TranscriptSource).MeetingId);
        }
        else
        {
            Assert.Equal(
                "call-1",
                Assert.IsType<TeamsAdhocCallTranscriptSource>(inboundEvent.TranscriptSource).CallId);
        }
    }

    private static ProcessTeamsInputsUsecaseHandler CreateHandler(
        TeamsInputConfiguration configuration,
        IReadOnlyList<InboundEvent> previousEvents,
        IReadOnlyList<TeamsActivitySnapshot> activities,
        RecordingCaptureService capture,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? skippedMeetings = null) => new(
            new MemoryRepository<TeamsInputConfiguration>([configuration]),
            new MemoryRepository<InboundEvent>(previousEvents),
            new StubTeamsReader(activities, errors ?? [], skippedMeetings ?? []),
            capture,
            new ImmediateLock(),
            new FixedTimeProvider(Now));

    private static TeamsInputConfiguration CreateConfiguration()
    {
        var configuration = new TeamsInputConfiguration
        {
            LastSuccessfulCheckAt = Now.AddMinutes(-10)
        };
        configuration.Update(
            "tenant-id",
            "client-id",
            null,
            5,
            2,
            "Work Teams guidance.",
            true,
            true,
            false,
            [],
            [],
            []);
        configuration.RecordConnectedAccount(
            "connected-user",
            "Michal Tichy",
            "michal@example.com");
        return configuration;
    }

    private static TeamsActivitySnapshot Message(
        string id,
        DateTimeOffset occurredAt,
        bool isNew,
        string body,
        string? replyToMessageId = null,
        string senderId = "person-1",
        string senderName = "Rosie") => new TeamsChatMessageSnapshot
        {
            ExternalId = $"chat:chat-1:{id}:v1",
            OccurredAt = occurredAt,
            BodyText = body,
            SenderId = senderId,
            SenderName = senderName,
            ChatId = "chat-1",
            ConversationName = "Rosie",
            MessageId = id,
            ReplyToMessageId = replyToMessageId,
            Version = "v1",
            IsNew = isNew
        };

    private sealed class StubTeamsReader(
        IReadOnlyList<TeamsActivitySnapshot> activities,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> skippedMeetings) : ITeamsInputReader
    {
        public Task<TeamsInputCatalog> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TeamsInputReadResult> ReadNewAsync(
            DateTimeOffset since,
            DateTimeOffset until,
            bool includeChatMessages,
            bool includeChannelMessages,
            bool includeCallTranscripts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TeamsInputReadResult(activities, errors, skippedMeetings));
    }

    private sealed class RecordingCaptureService : IInboundEventCaptureService
    {
        public List<InboundEvent> Events { get; } = [];

        public Task<InboundEventCaptureResult> CaptureAsync(
            InboundEvent inboundEvent,
            CancellationToken cancellationToken)
        {
            inboundEvent.Id = $"captured-{Events.Count + 1}";
            Events.Add(inboundEvent);
            return Task.FromResult(new InboundEventCaptureResult(inboundEvent.Id, false));
        }

    }

    private sealed class ImmediateLock : ITeamsInputProcessingLock
    {
        public Task<IDisposable> EnterAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IDisposable>(new Lease());

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryRepository<T>(IReadOnlyList<T> initial) : IRepository<T>
        where T : class, IItemWithId
    {
        private readonly List<T> items = [.. initial];

        public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult(items.SingleOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items.Where(item => ids.Contains(item.Id)).ToList());

        public Task<T?> GetBySpecAsync(ISpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items);

        public Task<IReadOnlyList<T>> ListAsync(IListSpecification<T> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<IReadOnlyList<T>>(items);

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<T, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult(items.Count);

        public Task AddAsync(ICollection<T> entities, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.AddRange(entities);
            return Task.CompletedTask;
        }

        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.CompletedTask;

        public Task GetAndUpdateAsync(string id, Func<T, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            updateMethod(items.Single(item => item.Id == id));

        public Task DeleteAsync(T entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.Remove(entity);
            return Task.CompletedTask;
        }

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            var set = ids.ToHashSet(StringComparer.Ordinal);
            items.RemoveAll(item => set.Contains(item.Id));
            return Task.CompletedTask;
        }
    }
}
