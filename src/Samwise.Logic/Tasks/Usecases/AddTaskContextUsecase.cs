using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record AddTaskContextUsecase(string TaskId, string Text) : IRequest;

internal sealed class AddTaskContextUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    IRepository<TaskComment> commentRepository,
    ISender sender,
    TimeProvider timeProvider) : IRequestHandler<AddTaskContextUsecase>
{
    public async Task Handle(AddTaskContextUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.AddContext(request.Text, timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);
        await commentRepository.AddAsync(new TaskComment
        {
            Id = Guid.NewGuid().ToString(), TaskId = task.Id, Body = request.Text.Trim(),
            AuthorKind = CommentAuthorKind.User, Kind = TaskCommentKind.Context,
            CreatedAt = timeProvider.GetUtcNow()
        }, cancellationToken);
        await sender.Send(new PlanTaskUsecase(task.Id), cancellationToken);
    }
}
