using MediatR;
using Samwise.Domain.Prompts;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record SavePromptTemplateUsecase(PromptStage Stage, string Content) : IRequest<PromptSaveResult>;

internal sealed class SavePromptTemplateUsecaseHandler(IPromptTemplateStore store)
    : IRequestHandler<SavePromptTemplateUsecase, PromptSaveResult>
{
    public Task<PromptSaveResult> Handle(SavePromptTemplateUsecase request, CancellationToken cancellationToken) =>
        store.SaveAsync(request.Stage, request.Content, cancellationToken);
}
