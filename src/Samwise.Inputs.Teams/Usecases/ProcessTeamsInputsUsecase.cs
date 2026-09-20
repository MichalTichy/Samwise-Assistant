using MediatR;
using Samwise.Domain.Inbox;
using Samwise.Inputs.Teams;
using Samwise.Logic.Inbox.Services;
using Samwise.Inputs.Teams.Events;
using Samwise.Inputs.Teams.Services;
using Samwise.Inputs.Teams.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Teams.Usecases;

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
    private const int MaximumContextCharacters = 30_000;

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
            return new TeamsProcessingResult(0, 0, 0, []);
        }

        var now = timeProvider.GetUtcNow();
        if (configuration.LastSuccessfulCheckAt is null)
        {
            configuration.RecordSuccessfulCheck(now);
            await configurationRepository.UpdateAsync(configuration, cancellationToken);
            return new TeamsProcessingResult(0, 0, 0, []);
        }

        var checkedThrough = now - TimeSpan.FromMinutes(configuration.DebounceMinutes);
        if (checkedThrough <= configuration.LastSuccessfulCheckAt)
        {
            return new TeamsProcessingResult(0, 0, 0, []);
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
            return new TeamsProcessingResult(0, 0, 0, [exception.Message]);
        }

        var imported = 0;
        var ignored = 0;
        var alreadyProcessed = 0;
        var hasDeferredConversation = false;

        var messageActivities = readResult.Activities
            .Where(activity => activity.Kind is TeamsActivityKind.ChatMessage or TeamsActivityKind.ChannelMessage)
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
                     .Where(activity => activity.Kind == TeamsActivityKind.CallTranscript)
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
        return new TeamsProcessingResult(imported, ignored, alreadyProcessed, readResult.Errors);
    }

    private async Task<(bool AlreadyExisted, bool Ignored, bool Captured, bool Deferred)> CaptureConversationAsync(
        string conversationKey,
        IReadOnlyList<TeamsActivitySnapshot> messages,
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
        var conversationIgnored = configuration.IsChatIgnored(first.ChatId)
            || configuration.IsChannelIgnored(first.ChannelId);
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
        var context = BuildContext(visibleContext, priorMessages);
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
        IReadOnlyList<TeamsActivitySnapshot> allMessages,
        IReadOnlyList<TeamsActivitySnapshot> triggerMessages,
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
        IReadOnlyList<TeamsActivitySnapshot> messages,
        IReadOnlyDictionary<string, TeamsConversationInboundEvent> priorMessages)
    {
        var result = new List<InboundEventContextItem>();
        var remaining = MaximumContextCharacters;
        foreach (var message in messages)
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
                SourceItemId = message.MessageId ?? message.ExternalId,
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

        result.Reverse();
        return result;
    }

    private static TeamsConversationMessage CreateMessage(TeamsActivitySnapshot activity) => new()
    {
        MessageId = activity.MessageId ?? activity.ExternalId,
        Version = activity.Version ?? string.Empty,
        ReplyToMessageId = activity.ReplyToMessageId,
        SenderId = activity.SenderId,
        SenderName = activity.SenderName ?? "Unknown",
        SentAt = activity.OccurredAt,
        BodyText = activity.BodyText,
        WebUrl = activity.WebUrl
    };

    private static TeamsConversationSource CreateConversationSource(TeamsActivitySnapshot activity) =>
        activity.Kind switch
        {
            TeamsActivityKind.ChatMessage => new TeamsChatConversationSource
            {
                ChatId = Required(activity.ChatId, "Teams chat ID"),
                ChatName = activity.ConversationName ?? "Microsoft Teams chat"
            },
            TeamsActivityKind.ChannelMessage => CreateChannelThreadSource(activity),
            _ => throw new InvalidOperationException("Only Teams messages have a conversation source.")
        };

    private static TeamsConversationSource CreateChannelThreadSource(TeamsActivitySnapshot activity)
    {
        var teamId = Required(activity.TeamId, "Teams team ID");
        var channelId = Required(activity.ChannelId, "Teams channel ID");
        var threadRootId = Required(
            activity.ReplyToMessageId ?? activity.MessageId,
            "Teams thread root message ID");
        return new TeamsChannelThreadConversationSource
        {
            TeamId = teamId,
            TeamName = activity.TeamName ?? "Microsoft Teams team",
            ChannelId = channelId,
            ChannelName = activity.ChannelName ?? activity.ConversationName ?? "Microsoft Teams channel",
            ThreadRootMessageId = threadRootId
        };
    }

    private static TeamsTranscriptInboundEvent CreateTranscriptEvent(
        TeamsActivitySnapshot activity,
        string sourceGuidance,
        bool isIgnored,
        DateTimeOffset now) => new()
        {
            TranscriptId = activity.TranscriptId ?? activity.ExternalId,
            TranscriptSource = CreateTranscriptSource(activity),
            OrganizerId = activity.SenderId ?? string.Empty,
            OrganizerName = activity.SenderName ?? "Unknown",
            CreatedAt = activity.OccurredAt,
            EndedAt = activity.EndedAt,
            ContextKey = activity.MeetingId ?? activity.CallId,
            SourceGuidance = sourceGuidance,
            ReceivedAt = now,
            Status = isIgnored ? InboundEventStatus.Processed : InboundEventStatus.Pending,
            HandledAt = isIgnored ? now : null,
            Explanation = isIgnored
                ? "Ignored by the Microsoft Teams input configuration."
                : "Waiting for intake classification."
        };

    private static TeamsTranscriptSource CreateTranscriptSource(TeamsActivitySnapshot activity)
    {
        if (string.Equals(activity.TranscriptKind, "scheduledMeeting", StringComparison.Ordinal))
        {
            return new TeamsScheduledMeetingTranscriptSource
            {
                MeetingId = Required(activity.MeetingId, "Teams meeting ID")
            };
        }

        if (string.Equals(activity.TranscriptKind, "adhocCall", StringComparison.Ordinal))
        {
            return new TeamsAdhocCallTranscriptSource
            {
                CallId = Required(activity.CallId, "Teams call ID")
            };
        }

        throw new InvalidOperationException(
            $"Unsupported Teams transcript kind '{activity.TranscriptKind ?? "<missing>"}'.");
    }

    private static string GetConversationKey(TeamsActivitySnapshot activity) => activity.Kind switch
    {
        TeamsActivityKind.ChatMessage => $"teams:chat:{activity.ChatId}",
        TeamsActivityKind.ChannelMessage =>
            $"teams:channel:{activity.TeamId}:{activity.ChannelId}:thread:{activity.ReplyToMessageId ?? activity.MessageId}",
        _ => throw new InvalidOperationException("Only Teams messages belong to a conversation group.")
    };

    private static string MessageKey(TeamsActivitySnapshot message) =>
        MessageKey(message.MessageId ?? message.ExternalId, message.Version ?? string.Empty);

    private static string MessageKey(string messageId, string version) => $"{messageId}:{version}";

    private static string FormatContextMessage(TeamsActivitySnapshot message) =>
        $"[{message.OccurredAt:O}] {message.SenderName ?? "Unknown"} (message {message.MessageId})\n{message.BodyText.Trim()}";

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{name} is required.");

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
