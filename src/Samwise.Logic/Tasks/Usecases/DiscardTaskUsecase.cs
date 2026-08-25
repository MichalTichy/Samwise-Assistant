using MediatR;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record DiscardTaskUsecase(string TaskId) : IRequest;

internal sealed class DiscardTaskUsecaseHandler(
    IRepository<DomainTask> taskRepository,
    TimeProvider timeProvider) : IRequestHandler<DiscardTaskUsecase>
{
    public async Task Handle(DiscardTaskUsecase request, CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetRequiredByIdAsync(request.TaskId, cancellationToken);
        task.Discard(timeProvider.GetUtcNow());
        await taskRepository.UpdateAsync(task, cancellationToken);
    }
}
