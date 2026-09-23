using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Teams.Ingestion;
using Samwise.Inputs.Teams.Ingestion.Activities;
using Samwise.Logic.Inbox.Services;
using Samwise.Inputs.Teams.InboundEvents.Conversations;
using Samwise.Inputs.Teams.InboundEvents.Transcripts;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Teams.Usecases.Processing;

public sealed record ProcessTeamsInputsUsecase : IRequest<TeamsProcessingResult>;

internal sealed class ProcessTeamsInputsUsecaseHandler(
    IRepository<TeamsInputConfiguration> configurationRepository,
    IReadOnlyRepository<InboundEvent> inboundEventRepository,
    ITeamsInputReader reader,
    IInboundEventCaptureService captureService,
    ITeamsInputProcessingLock processingLock,
    TimeProvider timeProvider)
    : IRequestHandler<ProcessTeamsInputsUsecase, TeamsProcessingResult>
{
    private const int MaximumContextCharacters = 12_000;
    private const int MaximumContextMessages = 30;

    public async Task<TeamsProcessingResult> Handle(
        ProcessTeamsInputsUsecase request,
        CancellationToken cancellationToken)
    {
        using var processingLease = await processingLock.EnterAsync(cancellationToken);
        var configuration = await configurationRepository.GetRequiredByIdAsync(
            TeamsInputConfiguration.DocumentId,
            cancellationToken);
        if (!configuration.IsConfigured)
        {
            return new TeamsProcessingResult(0, 0, 0, [], []);
        }

        var now = timeProvider.GetUtcNow();
        if (configuration.LastSuccessfulCheckAt is null)
        {
            configuration.RecordSuccessfulCheck(now);
            await configurationRepository.UpdateAsync(configuration, cancellationToken);
            return new TeamsProcessingResult(0, 0, 0, [], []);
        }

        var checkedThrough = now - TimeSpan.FromMinutes(configuration.DebounceMinutes);
        if (checkedThrough <= configuration.LastSuccessfulCheckAt)
        {
            return new TeamsProcessingResult(0, 0, 0, [], []);
        }

        TeamsInputReadResult readResult;
        try
        {
            readResult = await reader.ReadNewAsync(
                configuration.LastSuccessfulCheckAt.Value,
                checkedThrough,
                configuration.IncludeChatMessages,
                configuration.IncludeChannelMessages,
                configuration.IncludeCallTranscripts,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            configuration.RecordFailedCheck(exception.Message);
            await configurationRepository.UpdateAsync(configuration, cancellationToken);
            return new TeamsProcessingResult(0, 0, 0, [exception.Message], []);
        }

        var imported = 0;
        var ignored = 0;
        var alreadyProcessed = 0;
        var hasDeferredConversation = false;

        var messageActivities = readResult.Activities
            .OfType<TeamsMessageSnapshot>()
            .GroupBy(GetConversationKey, StringComparer.Ordinal);
        foreach (var conversation in messageActivities)
        {
            var result = await CaptureConversationAsync(
                conversation.Key,
                conversation.ToList(),
                configuration,
                checkedThrough,
                now,
                cancellationToken);
            hasDeferredConversation |= result.Deferred;
            AddResult(result, ref imported, ref ignored, ref alreadyProcessed);
        }

        foreach (var transcript in readResult.Activities
                     .OfType<TeamsTranscriptSnapshot>()
                     .OrderBy(activity => activity.OccurredAt))
        {
            var isIgnored = configuration.IsPersonIgnored(transcript.SenderId);
            var inboundEvent = CreateTranscriptEvent(transcript, configuration.IntakeGuidance, isIgnored, now);
            var result = await captureService.CaptureAsync(inboundEvent, cancellationToken);
            AddResult((result.AlreadyExisted, isIgnored, true, false), ref imported, ref ignored, ref alreadyProcessed);
        }

        if (readResult.Errors.Count == 0)
        {
            if (hasDeferredConversation)
            {
                configuration.RecordDeferredCheck();
            }
            else
            {
                configuration.RecordSuccessfulCheck(checkedThrough);
            }
        }
        else
        {
            configuration.RecordFailedCheck(string.Join(" ", readResult.Errors));
        }

        await configurationRepository.UpdateAsync(configuration, cancellationToken);
        return new TeamsProcessingResult(
            imported, ignored, alreadyProcessed, readResult.Errors, readResult.SkippedMeetings);
    }

    private async Task<(bool AlreadyExisted, bool Ignored, bool Captured, bool Deferred)> CaptureConversationAsync(
        string conversationKey,
        IReadOnlyList<TeamsMessageSnapshot> messages,
        TeamsInputConfiguration configuration,
        DateTimeOffset checkedThrough,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = messages.Where(message => message.IsNew).ToList();
        if (candidates.Count == 0)
        {
            return (false, false, false, false);
        }

        if (candidates.Max(message => message.OccurredAt) > checkedThrough)
        {
            return (false, false, false, true);
        }

        var previousEvents = (await inboundEventRepository.ListAsync(
                new TeamsConversationEventsSpecification(conversationKey),
                cancellationToken))
            .OfType<TeamsConversationInboundEvent>()
            .ToList();
        var priorMessages = previousEvents
            .SelectMany(input => input.Messages.Select(message =>
                (Key: MessageKey(message.MessageId, message.Version), Event: input)))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Event, StringComparer.Ordinal);
        var newMessages = candidates
            .Where(message => !priorMessages.ContainsKey(MessageKey(message)))
            .ToList();
        if (newMessages.Count == 0)
        {
            return (true, false, false, false);
        }

        var first = messages[0];
        var conversationIgnored = first switch
        {
            TeamsChatMessageSnapshot chat => configuration.IsChatIgnored(chat.ChatId),
            TeamsChannelMessageSnapshot channel => configuration.IsChannelIgnored(channel.ChannelId),
            _ => false
        };
        var triggerMessages = conversationIgnored
            ? newMessages
            : newMessages.Where(message => !configuration.IsPersonIgnored(message.SenderId)).ToList();
        var isIgnored = conversationIgnored || triggerMessages.Count == 0;
        if (isIgnored)
        {
            triggerMessages = newMessages;
        }

        var triggerKeys = triggerMessages.Select(MessageKey).ToHashSet(StringComparer.Ordinal);
        var visibleContext = messages
            .Where(message => !triggerKeys.Contains(MessageKey(message)))
            .Where(message => !configuration.IsPersonIgnored(message.SenderId))
            .OrderByDescending(message => message.OccurredAt)
            .ToList();
        var repliedToMessageIds = triggerMessages
            .Select(message => message.ReplyToMessageId)
            .Where(messageId => !string.IsNullOrWhiteSpace(messageId))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var context = BuildContext(visibleContext, priorMessages, repliedToMessageIds);
        var inboundEvent = CreateConversationEvent(
            messages,
            triggerMessages,
            context,
            configuration.IntakeGuidance,
            isIgnored,
            now);
        var capture = await captureService.CaptureAsync(inboundEvent, cancellationToken);
        return (capture.AlreadyExisted, isIgnored, true, false);
    }

    private static TeamsConversationInboundEvent CreateConversationEvent(
        IReadOnlyList<TeamsMessageSnapshot> allMessages,
        IReadOnlyList<TeamsMessageSnapshot> triggerMessages,
        List<InboundEventContextItem> context,
        string sourceGuidance,
        bool isIgnored,
        DateTimeOffset now)
    {
        var first = allMessages[0];
        var versionSource = string.Join(
            "\n",
            triggerMessages.Select(MessageKey).Order(StringComparer.Ordinal));
        var conversationVersion = InboundEvent.CreateDedupeKey("Teams conversation version", versionSource);
        var conversation = CreateConversationSource(first);
        return new TeamsConversationInboundEvent
        {
            ConversationVersion = conversationVersion,
            ContextKey = conversation.ConversationKey,
            Conversation = conversation,
            Messages = triggerMessages.OrderBy(message => message.OccurredAt).Select(CreateMessage).ToList(),
            Context = context,
            SourceGuidance = sourceGuidance,
            ReceivedAt = now,
            Status = isIgnored ? InboundEventStatus.Processed : InboundEventStatus.Pending,
            HandledAt = isIgnored ? now : null,
            Explanation = isIgnored
                ? "Ignored by the Microsoft Teams input configuration."
                : "Waiting for intake classification."
        };
    }

    private static List<InboundEventContextItem> BuildContext(
        IReadOnlyList<TeamsMessageSnapshot> messages,
        IReadOnlyDictionary<string, TeamsConversationInboundEvent> priorMessages,
        IReadOnlySet<string> repliedToMessageIds)
    {
        var result = new List<InboundEventContextItem>();
        var remaining = MaximumContextCharacters;
        var selectedMessages = messages
            .Where(message => repliedToMessageIds.Contains(message.MessageId))
            .Concat(messages.Where(message => !repliedToMessageIds.Contains(message.MessageId)))
            .DistinctBy(MessageKey)
            .Take(MaximumContextMessages);
        foreach (var message in selectedMessages)
        {
            var content = FormatContextMessage(message);
            if (content.Length > remaining)
            {
                if (result.Count == 0 && remaining > 1)
                {
                    content = content[..(remaining - 1)] + "…";
                }
                else
                {
                    break;
                }
            }

            priorMessages.TryGetValue(MessageKey(message), out var priorEvent);
            result.Add(new InboundEventContextItem
            {
                SourceItemId = message.MessageId,
                Content = content,
                OccurredAt = message.OccurredAt,
                PreviouslyProcessed = priorEvent is not null,
                InboundEventId = priorEvent?.Id,
                WorkItemIds = priorEvent?.Outcomes
                    .Select(outcome => outcome.WorkItemId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? []
            });
            remaining -= content.Length;
            if (remaining <= 0)
            {
                break;
            }
        }

        return result.OrderBy(item => item.OccurredAt).ToList();
    }

    private static TeamsConversationMessage CreateMessage(TeamsMessageSnapshot activity) => new()
    {
        MessageId = activity.MessageId,
        Version = activity.Version,
        ReplyToMessageId = activity.ReplyToMessageId,
        SenderId = activity.SenderId,
        SenderName = activity.SenderName ?? "Unknown",
        SentAt = activity.OccurredAt,
        BodyText = activity.BodyText,
        WebUrl = activity.WebUrl
    };

    private static TeamsConversationSource CreateConversationSource(TeamsMessageSnapshot activity) =>
        activity switch
        {
            TeamsChatMessageSnapshot chat => new TeamsChatConversationSource
            {
                ChatId = chat.ChatId,
                ChatName = chat.ConversationName
            },
            TeamsChannelMessageSnapshot channel => CreateChannelThreadSource(channel),
            _ => throw new InvalidOperationException("Only Teams messages have a conversation source.")
        };

    private static TeamsConversationSource CreateChannelThreadSource(TeamsChannelMessageSnapshot activity)
    {
        return new TeamsChannelThreadConversationSource
        {
            TeamId = activity.TeamId,
            TeamName = activity.TeamName,
            ChannelId = activity.ChannelId,
            ChannelName = activity.ChannelName,
            ThreadRootMessageId = activity.ReplyToMessageId ?? activity.MessageId
        };
    }

    private static TeamsTranscriptInboundEvent CreateTranscriptEvent(
        TeamsTranscriptSnapshot activity,
        string sourceGuidance,
        bool isIgnored,
        DateTimeOffset now) => new()
        {
            TranscriptId = activity.TranscriptId,
            TranscriptSource = CreateTranscriptSource(activity),
            OrganizerId = activity.SenderId ?? string.Empty,
            OrganizerName = activity.SenderName ?? "Unknown",
            CreatedAt = activity.OccurredAt,
            EndedAt = activity.EndedAt,
            ContextKey = activity switch
            {
                TeamsScheduledMeetingTranscriptSnapshot meeting => meeting.MeetingId,
                TeamsAdhocCallTranscriptSnapshot call => call.CallId,
                _ => activity.ExternalId
            },
            SourceGuidance = sourceGuidance,
            ReceivedAt = now,
            Status = isIgnored ? InboundEventStatus.Processed : InboundEventStatus.Pending,
            HandledAt = isIgnored ? now : null,
            Explanation = isIgnored
                ? "Ignored by the Microsoft Teams input configuration."
                : "Waiting for intake classification."
        };

    private static TeamsTranscriptSource CreateTranscriptSource(TeamsTranscriptSnapshot activity) =>
        activity switch
        {
            TeamsScheduledMeetingTranscriptSnapshot meeting => new TeamsScheduledMeetingTranscriptSource
            {
                MeetingId = meeting.MeetingId
            },
            TeamsAdhocCallTranscriptSnapshot call => new TeamsAdhocCallTranscriptSource
            {
                CallId = call.CallId
            },
            _ => throw new InvalidOperationException("Unsupported Teams transcript snapshot type.")
        };

    private static string GetConversationKey(TeamsMessageSnapshot activity) => activity switch
    {
        TeamsChatMessageSnapshot chat => $"teams:chat:{chat.ChatId}",
        TeamsChannelMessageSnapshot channel =>
            $"teams:channel:{channel.TeamId}:{channel.ChannelId}:thread:{channel.ReplyToMessageId ?? channel.MessageId}",
        _ => throw new InvalidOperationException("Only Teams messages belong to a conversation group.")
    };

    private static string MessageKey(TeamsMessageSnapshot message) =>
        MessageKey(message.MessageId, message.Version);

    private static string MessageKey(string messageId, string version) => $"{messageId}:{version}";

    private static string FormatContextMessage(TeamsMessageSnapshot message) =>
        $"[{message.OccurredAt:O}] {message.SenderName ?? "Unknown"} (message {message.MessageId})\n{message.BodyText.Trim()}";

    private static void AddResult(
        (bool AlreadyExisted, bool Ignored, bool Captured, bool Deferred) result,
        ref int imported,
        ref int ignored,
        ref int alreadyProcessed)
    {
        if (!result.Captured)
        {
            if (result.AlreadyExisted)
            {
                alreadyProcessed++;
            }

            return;
        }

        if (result.AlreadyExisted)
        {
            alreadyProcessed++;
        }
        else if (result.Ignored)
        {
            ignored++;
        }
        else
        {
            imported++;
        }
    }
}
