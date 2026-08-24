using MediatR;
using Samwise.Domain.Prompts;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Logic.Prompts.Usecases;

public sealed record SavePromptTemplateUsecase(PromptStage Stage, string Content) : IRequest<PromptSaveResult>;

internal sealed class SavePromptTemplateUsecaseHandler(
    IRepository<PromptTemplate> repository,
    IPromptRenderer renderer,
    TimeProvider timeProvider)
    : IRequestHandler<SavePromptTemplateUsecase, PromptSaveResult>
{
    public async Task<PromptSaveResult> Handle(
        SavePromptTemplateUsecase request,
        CancellationToken cancellationToken)
    {
        var errors = renderer.Validate(request.Stage, request.Content);
        if (errors.Count > 0)
        {
            return new PromptSaveResult(false, errors);
        }

        var id = request.Stage.ToString().ToLowerInvariant();
        var template = await repository.GetByIdAsync(id, cancellationToken);
        if (template is null)
        {
            template = new PromptTemplate
            {
                Id = id,
                Stage = request.Stage,
                Name = request.Stage.ToString(),
                Content = request.Content.Trim(),
                UpdatedAt = timeProvider.GetUtcNow()
            };
            await repository.AddAsync(template, cancellationToken);
        }
        else
        {
            template.Content = request.Content.Trim();
            template.UpdatedAt = timeProvider.GetUtcNow();
            await repository.UpdateAsync(template, cancellationToken);
        }

        return PromptSaveResult.Success;
    }
}
