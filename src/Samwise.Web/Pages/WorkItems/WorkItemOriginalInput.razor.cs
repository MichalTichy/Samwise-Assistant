using Samwise.Web.Components;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Inbox;
using Shared.Infrastructure.Persistence.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Web.Pages.WorkItems;

public partial class WorkItemOriginalInput : AppComponentBase
{
    private IReadOnlyList<InboundEvent> sourceEvents = [];

    [Parameter, EditorRequired] public Samwise.Domain.WorkItems.WorkItem WorkItem { get; set; } = default!;
    [Inject] private IReadOnlyRepository<InboundEvent> InboundEventRepository { get; set; } = default!;

    protected override Task OnAfterRenderAsync(bool firstRender) => firstRender && WorkItem.Sources.Count > 0
        ? ExecuteLoadAsync(ReadSourcesAsync, events => sourceEvents = events)
        : Task.CompletedTask;

    private async Task<IReadOnlyList<InboundEvent>> ReadSourcesAsync(CancellationToken token)
    {
        var ids = WorkItem.Sources.Select(source => source.InboundEventId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Reverse();

        var events = new List<InboundEvent>();
        foreach (var id in ids)
        {
            var source = await InboundEventRepository.GetByIdAsync(id, token);
            if (source is not null)
            {
                events.Add(source);
            }
        }

        return events;
    }
}
