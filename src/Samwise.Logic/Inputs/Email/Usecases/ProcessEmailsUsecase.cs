using MediatR;
using Microsoft.Extensions.Logging;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inputs.Email.Services;
using Samwise.Logic.Inputs.Email.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inputs.Email.Usecases;

public enum EmailSelection
{
    NewSinceCheckpoint,
    Unread
}

public sealed record ProcessEmailsUsecase(
    EmailSelection Selection,
    string? EmailAccountId = null) : IRequest<EmailProcessingResult>;

internal sealed class ProcessEmailsUsecaseHandler(
    IRepository<EmailAccount> accountRepository,
    IReadOnlyRepository<EmailInputConfiguration> configurationRepository,
    IEmailInboxReader inboxReader,
    IInboundEventCaptureService captureService,
    IEmailAccountProcessingLock accountProcessingLock,
    TimeProvider timeProvider,
    ILogger<ProcessEmailsUsecaseHandler> logger)
    : IRequestHandler<ProcessEmailsUsecase, EmailProcessingResult>
{
    public async Task<EmailProcessingResult> Handle(
        ProcessEmailsUsecase request,
        CancellationToken cancellationToken)
    {
        var accounts = await LoadAccountsAsync(request.EmailAccountId, cancellationToken);
        var configuration = await configurationRepository.GetRequiredByIdAsync(
            EmailInputConfiguration.DocumentId,
            cancellationToken);

        var accountsChecked = 0;
        var imported = 0;
        var ignored = 0;
        var alreadyProcessed = 0;
        var errors = new List<string>();

        foreach (var account in accounts)
        {
            using var lease = await accountProcessingLock.AcquireAsync(account.Id, cancellationToken);
            account.RecordCheckStarted(timeProvider.GetUtcNow());
            await accountRepository.UpdateAsync(account, cancellationToken);

            try
            {
                var readResult = await ReadAsync(
                    request.Selection,
                    account,
                    configuration.IgnoredSenderAddresses,
                    cancellationToken);
                accountsChecked++;

                foreach (var message in readResult.Messages)
                {
                    var isIgnored = configuration.IsIgnored(message.FromAddress);
                    var now = timeProvider.GetUtcNow();
                    var inboundEvent = CreateInboundEvent(account, message, isIgnored, now);
                    var result = await captureService.CaptureAsync(inboundEvent, cancellationToken);
                    if (result.AlreadyExisted)
                    {
                        alreadyProcessed++;
                    }
                    else if (isIgnored)
                    {
                        ignored++;
                    }
                    else
                    {
                        imported++;
                    }
                }

                if (readResult.NextCheckpoint is not null)
                {
                    account.InboxCheckpoint = readResult.NextCheckpoint;
                    if (readResult.CheckpointWasReset)
                    {
                        logger.LogWarning(
                            "The INBOX UID validity changed for email account {EmailAccountId}; "
                            + "Samwise established a new checkpoint without importing existing mail",
                            account.Id);
                    }
                }

                account.RecordCheckSucceeded(timeProvider.GetUtcNow());
                await accountRepository.UpdateAsync(account, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var message = $"{account.DisplayName}: {exception.Message}";
                errors.Add(message);
                account.RecordCheckFailed(exception.Message);
                await accountRepository.UpdateAsync(account, cancellationToken);
                logger.LogError(exception, "Checking email account {EmailAccountId} failed", account.Id);
            }
        }

        return new EmailProcessingResult(
            accountsChecked,
            imported,
            ignored,
            alreadyProcessed,
            errors);
    }

    private async Task<EmailReadResult> ReadAsync(
        EmailSelection selection,
        EmailAccount account,
        IReadOnlyCollection<string> ignoredSenderAddresses,
        CancellationToken cancellationToken)
    {
        if (selection == EmailSelection.NewSinceCheckpoint)
        {
            var result = await inboxReader.ReadNewAsync(
                account,
                ignoredSenderAddresses,
                cancellationToken);
            return new EmailReadResult(
                result.Messages,
                result.NextCheckpoint,
                result.CheckpointWasReset);
        }

        if (selection == EmailSelection.Unread)
        {
            var messages = await inboxReader.ReadUnreadAsync(
                account,
                ignoredSenderAddresses,
                cancellationToken);
            return new EmailReadResult(messages, null, false);
        }

        throw new ArgumentOutOfRangeException(nameof(selection), selection, null);
    }

    private async Task<IReadOnlyList<EmailAccount>> LoadAccountsAsync(
        string? emailAccountId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailAccountId))
        {
            return await accountRepository.ListAsync(
                new EmailAccountsSpecification(enabledOnly: true), cancellationToken);
        }

        var account = await accountRepository.GetByIdAsync(emailAccountId, cancellationToken)
            ?? throw new InvalidOperationException("The email account no longer exists.");
        return [account];
    }

    private static EmailInboundEvent CreateInboundEvent(
        EmailAccount account,
        EmailMessageSnapshot message,
        bool isIgnored,
        DateTimeOffset now) => new()
        {
            EmailAccountId = account.Id,
            AccountName = account.DisplayName,
            AccountEmailAddress = account.EmailAddress,
            Mailbox = message.Mailbox,
            UidValidity = message.UidValidity,
            Uid = message.Uid,
            MessageId = message.MessageId,
            Subject = message.Subject,
            FromAddress = message.FromAddress,
            FromDisplayName = message.FromDisplayName,
            ToAddresses = message.ToAddresses,
            SentAt = message.SentAt,
            EmailReceivedAt = message.ReceivedAt,
            WasReadWhenCaptured = message.WasRead,
            WasAnsweredWhenCaptured = message.WasAnswered,
            BodyText = message.BodyText,
            Attachments = message.Attachments,
            Replies = message.Replies,
            Handling = isIgnored ? InboundHandlingKind.Ignore : InboundHandlingKind.Pending,
            HandledAt = isIgnored ? now : null,
            Explanation = isIgnored
                ? $"Ignored because {message.FromAddress} is in the ignored senders list."
                : "Waiting for intake classification."
        };

    private sealed record EmailReadResult(
        IReadOnlyList<EmailMessageSnapshot> Messages,
        EmailInboxCheckpoint? NextCheckpoint,
        bool CheckpointWasReset);
}
