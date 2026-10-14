using MediatR;
namespace Samwise.Logic.WorkItems.Usecases;

public sealed record DiscardWorkItemUsecase(string WorkItemId, string? Reason = null) : IRequest;
