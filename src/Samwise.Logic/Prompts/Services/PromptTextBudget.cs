using Samwise.Domain.WorkItems;

namespace Samwise.Logic.Prompts.Services;

internal static class PromptTextBudget
{
    private const int MaximumInputCharacters = 24_000;
    private const int MaximumWorkItemDescriptionCharacters = 12_000;
    private const int MaximumCommentsCharacters = 12_000;

    public static string Input(string value) => HeadAndTail(value, MaximumInputCharacters);

    public static string WorkItemDescription(string value) =>
        HeadAndTail(value, MaximumWorkItemDescriptionCharacters);

    public static string Comments(IReadOnlyList<WorkItemComment> comments)
    {
        if (comments.Count == 0)
        {
            return string.Empty;
        }

        var selected = new List<string>();
        var remaining = MaximumCommentsCharacters;
        foreach (var comment in comments.OrderByDescending(comment => comment.CreatedAt))
        {
            var maximumBodyLength = comment.AuthorKind switch
            {
                CommentAuthorKind.User => 6_000,
                CommentAuthorKind.Samwise => 2_500,
                _ => 1_500
            };
            var body = HeadAndTail(comment.Body, maximumBodyLength);
            var formatted = $"- [{comment.CreatedAt:O}] [{comment.AuthorKind}/{comment.Kind}] {body}";
            if (formatted.Length > remaining)
            {
                continue;
            }

            selected.Add(formatted);
            remaining -= formatted.Length + Environment.NewLine.Length;
        }

        selected.Reverse();
        return string.Join(Environment.NewLine, selected);
    }

    internal static string HeadAndTail(string value, int maximumLength)
    {
        var text = value.Trim();
        if (text.Length <= maximumLength)
        {
            return text;
        }

        const string omission = "\n\n[Earlier source content omitted from the prompt; retrieve the source when needed.]\n\n";
        var available = maximumLength - omission.Length;
        var headLength = available * 2 / 3;
        var tailLength = available - headLength;
        return string.Concat(text.AsSpan(0, headLength), omission, text.AsSpan(text.Length - tailLength));
    }
}
