using MediatR;
using Samwise.Domain.Prompts;
using Samwise.Logic.Prompts.Specifications;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record GetPromptTemplatesUsecase : IRequest<IReadOnlyList<PromptTemplateEditor>>;

internal sealed class GetPromptTemplatesUsecaseHandler(
    IReadOnlyRepository<PromptTemplate> repository,
    IPromptRenderer renderer)
    : IRequestHandler<GetPromptTemplatesUsecase, IReadOnlyList<PromptTemplateEditor>>
{
    public async Task<IReadOnlyList<PromptTemplateEditor>> Handle(
        GetPromptTemplatesUsecase request,
        CancellationToken cancellationToken)
    {
        var templates = await repository.ListAsync(
            new PromptTemplatesSpecification(),
            cancellationToken);

        return templates.Select(template => new PromptTemplateEditor(
            template.Id,
            template.Stage,
            template.Name,
            template.Content,
            template.UpdatedAt,
            renderer.GetPlaceholders(template.Stage))).ToList();
    }
}
