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
    string Username,
    string? Password,
    bool IsEnabled,
    bool ProcessExistingUnread) : IRequest<EmailAccountSaveResult>;

internal sealed class SaveEmailAccountUsecaseHandler(
    IRepository<EmailAccount> repository,
    IEmailCredentialProtector credentialProtector,
    TimeProvider timeProvider) : IRequestHandler<SaveEmailAccountUsecase, EmailAccountSaveResult>
{
    public async Task<EmailAccountSaveResult> Handle(
        SaveEmailAccountUsecase request,
        CancellationToken cancellationToken)
    {
        var isNew = string.IsNullOrWhiteSpace(request.EmailAccountId);
        var errors = Validate(request, isNew);
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

        var now = timeProvider.GetUtcNow();
        if (account is null)
        {
            account = new EmailAccount
            {
                Id = Guid.NewGuid().ToString(),
                CreatedAt = now,
                ImportAfter = request.ProcessExistingUnread ? null : now
            };
        }

        account.DisplayName = request.DisplayName.Trim();
        account.EmailAddress = request.EmailAddress.Trim();
        account.Host = request.Host.Trim();
        account.Port = request.Port;
        account.Security = request.Security;
        account.Username = request.Username.Trim();
        account.IsEnabled = request.IsEnabled;
        account.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            account.ProtectedPassword = credentialProtector.Protect(request.Password);
        }

        if (isNew)
        {
            await repository.AddAsync(account, cancellationToken);
        }
        else
        {
            await repository.UpdateAsync(account, cancellationToken);
        }

        return new EmailAccountSaveResult(true, account.Id, []);
    }

    private static List<string> Validate(SaveEmailAccountUsecase request, bool isNew)
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

        if (isNew && string.IsNullOrWhiteSpace(request.Password))
        {
            errors.Add("The password or app password is required for a new account.");
        }

        return errors;
    }
}
