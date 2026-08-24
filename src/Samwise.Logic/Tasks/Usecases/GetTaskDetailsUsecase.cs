using MediatR;
using Samwise.Domain.Agents;
using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainTask = Samwise.Domain.Tasks.Task;

namespace Samwise.Logic.Tasks.Usecases;

public sealed record GetTaskDetailsUsecase(string TaskId) : IRequest<TaskDetails?>;

internal sealed class GetTaskDetailsUsecaseHandler(
    IReadOnlyRepository<DomainTask> taskRepository,
    IReadOnlyRepository<TaskPlan> planRepository,
    IReadOnlyRepository<TaskComment> commentRepository,
    IReadOnlyRepository<PlanDecision> decisionRepository,
    IReadOnlyRepository<AgentInvocation> invocationRepository)
    : IRequestHandler<GetTaskDetailsUsecase, TaskDetails?>
{
    public async Task<TaskDetails?> Handle(
        GetTaskDetailsUsecase request,
        CancellationToken cancellationToken)
    {
        var task = await taskRepository.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var plan = task.CurrentPlanId is { } planId
            ? await planRepository.GetByIdAsync(planId, cancellationToken)
            : null;
        var comments = await commentRepository.ListAsync(
            new TaskCommentsSpecification(task.Id),
            cancellationToken);
        var decisions = await decisionRepository.ListAsync(
            new TaskDecisionsSpecification(task.Id),
            cancellationToken);
        var invocations = await invocationRepository.ListAsync(
            new TaskInvocationsSpecification(task.Id),
            cancellationToken);

        return new TaskDetails(task, plan, comments, decisions, invocations);
    }
}
