using Samwise.Domain.Prompts;

namespace Samwise.Logic.Prompts;

public interface IPromptTemplateStore
{
    Task<IReadOnlyList<PromptTemplateEditor>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PromptTemplateEditor?> GetAsync(PromptStage stage, CancellationToken cancellationToken = default);

    Task<PromptSaveResult> SaveAsync(
        PromptStage stage,
        string content,
        CancellationToken cancellationToken = default);
}

public interface IPromptRenderer
{
    IReadOnlyList<PromptPlaceholder> GetPlaceholders(PromptStage stage);

    IReadOnlyList<string> Validate(PromptStage stage, string template);

    PromptRenderResult Render(
        PromptStage stage,
        string template,
        IReadOnlyDictionary<string, string?> values);
}
