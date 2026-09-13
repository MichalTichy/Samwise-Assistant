using MediatR;

namespace Samwise.Logic.Inputs.Email.Usecases;

public sealed record StartMicrosoftEmailAuthorizationUsecase
    : IRequest<MicrosoftEmailAuthorizationStartResult>;

internal sealed class StartMicrosoftEmailAuthorizationUsecaseHandler(
    IMicrosoftEmailAuthorizationService authorizationService)
    : IRequestHandler<StartMicrosoftEmailAuthorizationUsecase, MicrosoftEmailAuthorizationStartResult>
{
    public async Task<MicrosoftEmailAuthorizationStartResult> Handle(
        StartMicrosoftEmailAuthorizationUsecase request,
        CancellationToken cancellationToken)
    {
        try
        {
            var challenge = await authorizationService.StartAsync(cancellationToken);
            return new MicrosoftEmailAuthorizationStartResult(true, challenge, []);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MicrosoftEmailAuthorizationStartResult(false, null, [exception.Message]);
        }
    }
}
