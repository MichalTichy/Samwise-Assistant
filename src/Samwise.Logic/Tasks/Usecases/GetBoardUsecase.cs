using MediatR;
using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record GetBoardUsecase(bool IncludeArchived = false) : IRequest<IReadOnlyList<TaskCard>>;

internal sealed class GetBoardUsecaseHandler(IReadOnlyRepository<DomainTask> taskRepository)
    : IRequestHandler<GetBoardUsecase, IReadOnlyList<TaskCard>>
{
    public async Task<IReadOnlyList<TaskCard>> Handle(
        GetBoardUsecase request,
        CancellationToken cancellationToken)
    {
        var tasks = await taskRepository.ListAsync(
            new BoardTasksSpecification(request.IncludeArchived),
            cancellationToken);
        var result = new List<TaskCard>(tasks.Count);
        foreach (var task in tasks)
        {
            var plan = task.FindCurrentPlan();
            result.Add(new TaskCard(
                task.Id,
                task.Title,
                task.Description,
                task.Status,
                task.Revision,
                task.UpdatedAt,
                plan?.PlanNumber));
        }

        return result;
    }
}
