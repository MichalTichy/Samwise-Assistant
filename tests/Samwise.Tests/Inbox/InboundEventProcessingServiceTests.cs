using Samwise.Logic.Inbox.Services;
using Samwise.Logic.WorkItems.Services;

namespace Samwise.Tests.Inbox;

public sealed class InboundEventProcessingServiceTests
{
    [Fact]
    public async Task PlansTheActionableWorkItemReturnedByClassification()
    {
        var classification = new StubClassificationService("work-item-1");
        var planning = new StubPlanningService();
        var service = new InboundEventProcessingService(classification, planning);

        await service.ProcessAsync("event-1", CancellationToken.None);

        Assert.Equal("event-1", classification.InboundEventId);
        Assert.Equal("work-item-1", planning.WorkItemId);
    }

    [Fact]
    public async Task StopsAfterClassificationWhenNoWorkItemIsActionable()
    {
        var classification = new StubClassificationService(null);
        var planning = new StubPlanningService();
        var service = new InboundEventProcessingService(classification, planning);

        await service.ProcessAsync("event-1", CancellationToken.None);

        Assert.Equal("event-1", classification.InboundEventId);
        Assert.Null(planning.WorkItemId);
    }

    private sealed class StubClassificationService(string? result) : IInboundEventClassificationService
    {
        public string? InboundEventId { get; private set; }

        public Task<string?> ClassifyAsync(string inboundEventId, CancellationToken cancellationToken)
        {
            InboundEventId = inboundEventId;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPlanningService : IWorkItemPlanningService
    {
        public string? WorkItemId { get; private set; }

        public Task PlanAsync(string workItemId, CancellationToken cancellationToken)
        {
            WorkItemId = workItemId;
            return Task.CompletedTask;
        }
    }
}
