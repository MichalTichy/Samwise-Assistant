using Samwise.Domain.Prompts;

namespace Samwise.Logic.Prompts;

public interface IPromptRenderer
{
    IReadOnlyList<PromptPlaceholder> GetPlaceholders(PromptStage stage);

    IReadOnlyList<string> Validate(PromptStage stage, string template);

    PromptRenderResult Render(
        PromptStage stage,
        string template,
        IReadOnlyDictionary<string, string?> values);
}
