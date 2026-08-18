namespace Samwise.Logic.Tasks;

public interface ITaskStore
{
    Task<IReadOnlyList<TaskCard>> GetBoardAsync(
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    Task<TaskDetails?> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboxEventItem>> GetInboxAsync(CancellationToken cancellationToken = default);

    Task AddContextAsync(Guid taskId, string text, CancellationToken cancellationToken = default);

    Task ApprovePlanAsync(
        Guid taskId,
        Guid planId,
        int taskRevision,
        CancellationToken cancellationToken = default);

    Task RejectPlanAsync(
        Guid taskId,
        Guid planId,
        int taskRevision,
        string feedback,
        CancellationToken cancellationToken = default);

    Task DiscardTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task RetryPlanningAsync(Guid taskId, CancellationToken cancellationToken = default);
}
