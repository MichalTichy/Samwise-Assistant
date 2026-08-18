using Marten;
using Samwise.Logic.Prompts;
using Samwise.Logic.Time;
using Samwise.Domain.Prompts;
using Samwise.Infrastructure.Persistence.Marten.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Prompts;

internal sealed class MartenPromptTemplateStore(
    IDocumentStore store,
    IPromptRenderer renderer,
    IClock clock) : IPromptTemplateStore
{
    public async Task<IReadOnlyList<PromptTemplateEditor>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var templates = await new PromptTemplatesSpecification()
            .ApplyAsync(session.Query<PromptTemplate>(), cancellationToken);

        return templates.Select(ToEditor).ToList();
    }

    public async Task<PromptTemplateEditor?> GetAsync(
        PromptStage stage,
        CancellationToken cancellationToken = default)
    {
        await using var session = store.LightweightSession();
        var template = await session.LoadAsync<PromptTemplate>(GetId(stage), cancellationToken);
        return template is null ? null : ToEditor(template);
    }

    public async Task<PromptSaveResult> SaveAsync(
        PromptStage stage,
        string content,
        CancellationToken cancellationToken = default)
    {
        var errors = renderer.Validate(stage, content);
        if (errors.Count > 0)
        {
            return new PromptSaveResult(false, errors);
        }

        await using var session = store.LightweightSession();
        var id = GetId(stage);
        var template = await session.LoadAsync<PromptTemplate>(id, cancellationToken)
            ?? new PromptTemplate { Id = id, Stage = stage, Name = stage.ToString() };

        template.Content = content.Trim();
        template.UpdatedAt = clock.UtcNow;
        session.Store(template);
        await session.SaveChangesAsync(cancellationToken);
        return PromptSaveResult.Success;
    }

    private PromptTemplateEditor ToEditor(PromptTemplate template) =>
        new(
            template.Id,
            template.Stage,
            template.Name,
            template.Content,
            template.UpdatedAt,
            renderer.GetPlaceholders(template.Stage));

    internal static string GetId(PromptStage stage) => stage.ToString().ToLowerInvariant();
}
