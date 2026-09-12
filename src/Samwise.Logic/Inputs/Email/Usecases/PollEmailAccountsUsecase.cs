using MediatR;
using Samwise.Logic.Inputs.Email.Services;

namespace Samwise.Logic.Inputs.Email.Usecases;

public sealed record PollEmailAccountsUsecase : IRequest<EmailProcessingResult>;

internal sealed class PollEmailAccountsUsecaseHandler(
    IEmailInputProcessingService processingService)
    : IRequestHandler<PollEmailAccountsUsecase, EmailProcessingResult>
{
    public Task<EmailProcessingResult> Handle(
        PollEmailAccountsUsecase request,
        CancellationToken cancellationToken) =>
        processingService.ProcessNewAsync(null, cancellationToken);
}
