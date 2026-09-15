using MediatR;
using Samwise.Logic.WorkItems.Services;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Logic.WorkItems.Usecases;

public sealed record AnswerPlanningQuestionUsecase(
    string WorkItemId,
    string QuestionId,
    string? OptionId,
    string? OtherAnswer) : IRequest;

internal sealed class AnswerPlanningQuestionUsecaseHandler(
    IRepository<DomainWorkItem> workItemRepository,
    IWorkItemPlanningService planningService,
    TimeProvider timeProvider) : IRequestHandler<AnswerPlanningQuestionUsecase>
{
    public async Task Handle(AnswerPlanningQuestionUsecase request, CancellationToken cancellationToken)
    {
        var workItem = await workItemRepository.GetRequiredByIdAsync(request.WorkItemId, cancellationToken);
        workItem.AnswerPlanningQuestion(
            request.QuestionId,
            request.OptionId,
            request.OtherAnswer,
            timeProvider.GetUtcNow());
        await workItemRepository.UpdateAsync(workItem, cancellationToken);
        await planningService.PlanAsync(workItem.Id, cancellationToken);
    }
}
