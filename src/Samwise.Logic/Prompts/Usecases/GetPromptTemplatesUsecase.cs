using MediatR;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record GetPromptTemplatesUsecase : IRequest<IReadOnlyList<PromptTemplateEditor>>;

internal sealed class GetPromptTemplatesUsecaseHandler(IPromptTemplateStore store)
    : IRequestHandler<GetPromptTemplatesUsecase, IReadOnlyList<PromptTemplateEditor>>
{
    public Task<IReadOnlyList<PromptTemplateEditor>> Handle(
        GetPromptTemplatesUsecase request,
        CancellationToken cancellationToken) =>
        store.GetAllAsync(cancellationToken);
}
