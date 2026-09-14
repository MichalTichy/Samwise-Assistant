using MediatR;
using Samwise.Domain.Inputs.Email;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inputs.Email.Usecases;

public sealed record SaveEmailAccountUsecase(
    string? EmailAccountId,
    string DisplayName,
    string EmailAddress,
    string Host,
    int Port,
    EmailConnectionSecurity Security,
    EmailAuthenticationMethod AuthenticationMethod,
    string Username,
    string? Password,
    string? MicrosoftAuthorizationId,
    bool IsEnabled) : IRequest<EmailAccountSaveResult>;

internal sealed class SaveEmailAccountUsecaseHandler(
    IRepository<EmailAccount> repository,
    IEmailCredentialProtector credentialProtector,
    IEmailInboxReader inboxReader,
    IMicrosoftEmailAuthorizationService microsoftAuthorizationService,
    TimeProvider timeProvider) : IRequestHandler<SaveEmailAccountUsecase, EmailAccountSaveResult>
{
    public async Task<EmailAccountSaveResult> Handle(
        SaveEmailAccountUsecase request,
        CancellationToken cancellationToken)
    {
        var isNew = string.IsNullOrWhiteSpace(request.EmailAccountId);
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new EmailAccountSaveResult(false, request.EmailAccountId, errors);
        }

        var account = isNew
            ? null
            : await repository.GetByIdAsync(request.EmailAccountId!, cancellationToken);
        if (!isNew && account is null)
        {
            return new EmailAccountSaveResult(false, request.EmailAccountId, ["The email account no longer exists."]);
        }

        var changingAuthenticationMethod = account is not null
            && account.AuthenticationMethod != request.AuthenticationMethod;
        if (request.AuthenticationMethod == EmailAuthenticationMethod.Password
            && string.IsNullOrWhiteSpace(request.Password)
            && (isNew || changingAuthenticationMethod || string.IsNullOrWhiteSpace(account?.ProtectedPassword)))
        {
            return new EmailAccountSaveResult(
                false,
                request.EmailAccountId,
                ["The password or app password is required for password authentication."]);
        }

        MicrosoftEmailAuthorizationCompletion? microsoftAuthorization = null;
        if (request.AuthenticationMethod == EmailAuthenticationMethod.Microsoft365OAuth)
        {
            if (!string.IsNullOrWhiteSpace(request.MicrosoftAuthorizationId))
            {
                microsoftAuthorization = await microsoftAuthorizationService.CompleteAsync(
                    request.MicrosoftAuthorizationId,
                    cancellationToken);
                if (microsoftAuthorization.State != MicrosoftEmailAuthorizationState.Succeeded)
                {
                    var message = microsoftAuthorization.State == MicrosoftEmailAuthorizationState.Pending
                        ? "Microsoft sign-in has not finished yet. Complete it in the Microsoft window, then try again."
                        : microsoftAuthorization.Error ?? "Microsoft sign-in did not complete.";
                    return new EmailAccountSaveResult(false, request.EmailAccountId, [message]);
                }
            }
            else if (isNew
                || changingAuthenticationMethod
                || string.IsNullOrWhiteSpace(account?.ProtectedOAuthTokenCache))
            {
                return new EmailAccountSaveResult(
                    false,
                    request.EmailAccountId,
                    ["Connect the account to Microsoft 365 before saving it."]);
            }
        }

        var connectionChanged = account is null
            || ConnectionChanged(account, request)
            || !string.IsNullOrWhiteSpace(request.Password)
            || microsoftAuthorization is not null;
        var now = timeProvider.GetUtcNow();
        if (account is null)
        {
            account = new EmailAccount
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = now
            };
        }

        account.DisplayName = request.DisplayName.Trim();
        account.EmailAddress = request.EmailAddress.Trim();
        account.Host = request.Host.Trim();
        account.Port = request.Port;
        account.Security = request.Security;
        account.AuthenticationMethod = request.AuthenticationMethod;
        account.Username = request.Username.Trim();
        account.IsEnabled = request.IsEnabled;
        account.UpdatedAt = now;
        if (request.AuthenticationMethod == EmailAuthenticationMethod.Password)
        {
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                account.ProtectedPassword = credentialProtector.Protect(request.Password);
            }

            account.ProtectedOAuthTokenCache = string.Empty;
            account.OAuthHomeAccountId = null;
        }
        else if (microsoftAuthorization is not null)
        {
            account.ProtectedPassword = string.Empty;
            account.ProtectedOAuthTokenCache = credentialProtector.Protect(microsoftAuthorization.TokenCache!);
            account.OAuthHomeAccountId = microsoftAuthorization.HomeAccountId;
            if (!string.IsNullOrWhiteSpace(microsoftAuthorization.Username))
            {
                account.Username = microsoftAuthorization.Username;
            }
        }

        if (connectionChanged || account.InboxCheckpoint is null)
        {
            try
            {
                account.InboxCheckpoint = await inboxReader.GetCurrentInboxCheckpointAsync(
                    account,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new EmailAccountSaveResult(
                    false,
                    request.EmailAccountId,
                    [$"The INBOX could not be initialized: {exception.Message}"]);
            }
        }

        if (connectionChanged || account.SentCheckpoint is null)
        {
            try
            {
                account.SentCheckpoint = await inboxReader.GetCurrentSentCheckpointAsync(
                    account,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new EmailAccountSaveResult(
                    false,
                    request.EmailAccountId,
                    [$"The Sent mailbox could not be initialized: {exception.Message}"]);
            }
        }

        if (isNew)
        {
            await repository.AddAsync(account, cancellationToken);
        }
        else
        {
            await repository.UpdateAsync(account, cancellationToken);
        }

        if (microsoftAuthorization is not null)
        {
            microsoftAuthorizationService.Finish(request.MicrosoftAuthorizationId!);
        }

        return new EmailAccountSaveResult(true, account.Id, []);
    }

    private static bool ConnectionChanged(EmailAccount account, SaveEmailAccountUsecase request) =>
        !string.Equals(account.Host, request.Host.Trim(), StringComparison.OrdinalIgnoreCase)
        || account.Port != request.Port
        || account.Security != request.Security
        || account.AuthenticationMethod != request.AuthenticationMethod
        || !string.Equals(account.Username, request.Username.Trim(), StringComparison.Ordinal);

    private static List<string> Validate(SaveEmailAccountUsecase request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors.Add("The account name cannot be empty.");
        }

        if (!System.Net.Mail.MailAddress.TryCreate(request.EmailAddress?.Trim(), out var parsed)
            || !string.Equals(parsed.Address, request.EmailAddress?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Enter a valid account email address.");
        }

        if (string.IsNullOrWhiteSpace(request.Host))
        {
            errors.Add("The IMAP host cannot be empty.");
        }

        if (request.Port is < 1 or > 65_535)
        {
            errors.Add("The IMAP port must be between 1 and 65,535.");
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors.Add("The IMAP username cannot be empty.");
        }

        return errors;
    }
}
