using Samwise.Domain.WorkItems;
using Samwise.Logic.Prompts.Services;

namespace Samwise.Tests.Prompts;

public sealed class PromptTextBudgetTests
{
    [Fact]
    public void LongSourceTextKeepsTheBeginningAndEndWithinTheBudget()
    {
        var source = "BEGIN" + new string('x', 40_000) + "END";

        var result = PromptTextBudget.Input(source);

        Assert.True(result.Length <= 24_000);
        Assert.StartsWith("BEGIN", result, StringComparison.Ordinal);
        Assert.EndsWith("END", result, StringComparison.Ordinal);
        Assert.Contains("omitted from the prompt", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentBudgetPrefersRecentUserDecisions()
    {
        var comments = Enumerable.Range(0, 20)
            .Select(index => new WorkItemComment
            {
                Id = index.ToString(),
                Body = "OLD-" + index + new string('x', 1_000),
                AuthorKind = CommentAuthorKind.System,
                Kind = WorkItemCommentKind.Context,
                CreatedAt = DateTimeOffset.UnixEpoch.AddMinutes(index)
            })
            .Append(new WorkItemComment
            {
                Id = "decision",
                Body = "LATEST-USER-DECISION",
                AuthorKind = CommentAuthorKind.User,
                Kind = WorkItemCommentKind.PlanningDecision,
                CreatedAt = DateTimeOffset.UnixEpoch.AddHours(1)
            })
            .ToArray();

        var result = PromptTextBudget.Comments(comments);

        Assert.True(result.Length <= 12_000);
        Assert.Contains("LATEST-USER-DECISION", result, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD-0", result, StringComparison.Ordinal);
    }
}
