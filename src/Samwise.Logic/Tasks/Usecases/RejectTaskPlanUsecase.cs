using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record RejectTaskPlanUsecase(
    string TaskId,
    string PlanId,
    int TaskRevision,
    string Feedback) : IRequest;

internal sealed class RejectTaskPlanUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    IRepository<PlanDecision> decisionRepository,
    IRepository<TaskComment> commentRepository,
    ISender sender,
    TimeProvider timeProvider) : IRequestHandler<RejectTaskPlanUsecase>
{
    public async Task Handle(RejectTaskPlanUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.Reject(request.PlanId, request.TaskRevision, request.Feedback, timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);
        await decisionRepository.AddAsync(new PlanDecision
        {
            Id = Guid.NewGuid().ToString(), TaskId = task.Id, PlanId = request.PlanId,
            TaskRevision = request.TaskRevision, Decision = PlanDecisionKind.Rejected,
            Feedback = request.Feedback.Trim(), DecidedAt = timeProvider.GetUtcNow()
        }, cancellationToken);
        await commentRepository.AddAsync(new TaskComment
        {
            Id = Guid.NewGuid().ToString(), TaskId = task.Id, Body = request.Feedback.Trim(),
            AuthorKind = CommentAuthorKind.User, Kind = TaskCommentKind.PlanningFeedback,
            CreatedAt = timeProvider.GetUtcNow()
        }, cancellationToken);
        await sender.Send(new PlanTaskUsecase(task.Id), cancellationToken);
    }
}
