using MediatR;
using Samwise.Inputs.Email;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Inputs.Email.Usecases;

public sealed record SaveEmailInputConfigurationUsecase(
    int PollingIntervalMinutes,
    IReadOnlyList<string> IgnoredSenderAddresses) : IRequest<EmailConfigurationSaveResult>;

internal sealed class SaveEmailInputConfigurationUsecaseHandler(
    IRepository<EmailInputConfiguration> repository)
    : IRequestHandler<SaveEmailInputConfigurationUsecase, EmailConfigurationSaveResult>
{
    public async Task<EmailConfigurationSaveResult> Handle(
        SaveEmailInputConfigurationUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new EmailConfigurationSaveResult(false, errors);
        }

        var configuration = await repository.GetRequiredByIdAsync(
            EmailInputConfiguration.DocumentId,
            cancellationToken);
        configuration.Update(request.PollingIntervalMinutes, request.IgnoredSenderAddresses);
        await repository.UpdateAsync(configuration, cancellationToken);
        return new EmailConfigurationSaveResult(true, []);
    }

    private static List<string> Validate(SaveEmailInputConfigurationUsecase request)
    {
        var errors = new List<string>();
        if (request.PollingIntervalMinutes is < 1 or > 1_440)
        {
            errors.Add("The polling interval must be between 1 and 1,440 minutes.");
        }

        foreach (var address in request.IgnoredSenderAddresses)
        {
            if (!System.Net.Mail.MailAddress.TryCreate(address?.Trim(), out var parsed)
                || !string.Equals(parsed.Address, address?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"'{address}' is not a valid sender email address.");
            }
        }

        return errors;
    }
}
