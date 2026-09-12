using Microsoft.Extensions.Logging;
using Samwise.Domain.Inbox;
using Samwise.Domain.Inputs.Email;
using Samwise.Logic.Inbox.Services;
using Samwise.Logic.Inputs.Email.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inputs.Email.Services;

internal interface IEmailInputProcessingService
{
    Task<EmailProcessingResult> ProcessUnreadAsync(
        string? emailAccountId,
        CancellationToken cancellationToken);
}

internal sealed class EmailInputProcessingService(
    IRepository<EmailAccount> accountRepository,
    IReadOnlyRepository<EmailInputConfiguration> configurationRepository,
    IEmailInboxReader inboxReader,
    IInboundEventCaptureService captureService,
    IEmailAccountProcessingLock accountProcessingLock,
    TimeProvider timeProvider,
    ILogger<EmailInputProcessingService> logger) : IEmailInputProcessingService
{
    public async Task<EmailProcessingResult> ProcessUnreadAsync(
        string? emailAccountId,
        CancellationToken cancellationToken)
    {
        var accounts = await LoadAccountsAsync(emailAccountId, cancellationToken);
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
                var messages = await inboxReader.ReadUnreadAsync(
                    account,
                    configuration.IgnoredSenderAddresses,
                    cancellationToken);
                accountsChecked++;
                foreach (var message in messages)
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
        UnreadEmailMessage message,
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
            BodyText = message.BodyText,
            Attachments = message.Attachments,
            Handling = isIgnored ? InboundHandlingKind.Ignore : InboundHandlingKind.Pending,
            HandledAt = isIgnored ? now : null,
            Explanation = isIgnored
                ? $"Ignored because {message.FromAddress} is in the ignored senders list."
                : "Waiting for intake classification."
        };
}
