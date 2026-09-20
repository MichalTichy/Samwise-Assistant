using Samwise.Logic.Inbox.Services;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Tests.Inbox;

public sealed class InboundEventProcessingServiceTests
{
    [Fact]
    public async Task PlansTheActionableWorkItemReturnedByClassification()
    {
        var classification = new StubClassificationService(["work-item-1", "work-item-2"]);
        var planning = new StubPlanningService();
        var service = new InboundEventProcessingService(classification, planning);

        await service.ProcessAsync("event-1", CancellationToken.None);

        Assert.Equal("event-1", classification.InboundEventId);
        Assert.Equal(["work-item-1", "work-item-2"], planning.WorkItemIds);
    }

    [Fact]
    public async Task StopsAfterClassificationWhenNoWorkItemIsActionable()
    {
        var classification = new StubClassificationService([]);
        var planning = new StubPlanningService();
        var service = new InboundEventProcessingService(classification, planning);

        await service.ProcessAsync("event-1", CancellationToken.None);

        Assert.Equal("event-1", classification.InboundEventId);
        Assert.Empty(planning.WorkItemIds);
    }

    private sealed class StubClassificationService(IReadOnlyList<string> result) : IInboundEventClassificationService
    {
        public string? InboundEventId { get; private set; }

        public Task<IReadOnlyList<string>> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken)
        {
            InboundEventId = inboundEventId;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPlanningService : IWorkItemPlanningService
    {
        public List<string> WorkItemIds { get; } = [];

        public Task PlanAsync(string workItemId, CancellationToken cancellationToken)
        {
            WorkItemIds.Add(workItemId);
            return Task.CompletedTask;
        }
    }
}
