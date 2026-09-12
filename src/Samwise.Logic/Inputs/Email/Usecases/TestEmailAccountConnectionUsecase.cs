using MediatR;
using Samwise.Domain.Inputs.Email;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Inputs.Email.Usecases;

public sealed record TestEmailAccountConnectionUsecase(string EmailAccountId) : IRequest<EmailOperationResult>;

internal sealed class TestEmailAccountConnectionUsecaseHandler(
    IReadOnlyRepository<EmailAccount> repository,
    IEmailInboxReader inboxReader)
    : IRequestHandler<TestEmailAccountConnectionUsecase, EmailOperationResult>
{
    public async Task<EmailOperationResult> Handle(
        TestEmailAccountConnectionUsecase request,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(request.EmailAccountId, cancellationToken);
        if (account is null)
        {
            return new EmailOperationResult(false, "The email account no longer exists.");
        }

        try
        {
            await inboxReader.TestConnectionAsync(account, cancellationToken);
            return new EmailOperationResult(true, "Connection successful.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new EmailOperationResult(false, exception.Message);
        }
    }
}
