using MediatR;
using Samwise.Logic.Inputs.Email.Services;

namespace Samwise.Logic.Inputs.Email.Usecases;

public sealed record ProcessUnreadEmailsUsecase(string? EmailAccountId = null)
    : IRequest<EmailProcessingResult>;

internal sealed class ProcessUnreadEmailsUsecaseHandler(
    IEmailInputProcessingService processingService)
    : IRequestHandler<ProcessUnreadEmailsUsecase, EmailProcessingResult>
{
    public Task<EmailProcessingResult> Handle(
        ProcessUnreadEmailsUsecase request,
        CancellationToken cancellationToken) =>
        processingService.ProcessUnreadAsync(request.EmailAccountId, cancellationToken);
}
