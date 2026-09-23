using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Web.Components.Pages;

public partial class DiscardedWorkItems
{
    private IReadOnlyList<DomainWorkItem> workItems = [];
    private bool loading = true;
    private string? restoringWorkItemId;
    private string? error;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private ISender Sender { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadAsync();
            StateHasChanged();
        }
    }

    private async Task LoadAsync()
    {
        loading = true;
        error = null;
        try
        {
            workItems = await WorkItemRepository.ListAsync(new DiscardedWorkItemsSpecification());
        }
        catch (Exception exception)
        {
            error = $"Discarded Work Items could not be loaded: {exception.Message}";
        }
        finally
        {
            loading = false;
        }
    }

    private async Task RestoreAsync(string workItemId)
    {
        restoringWorkItemId = workItemId;
        error = null;
        try
        {
            await Sender.Send(new RestoreWorkItemUsecase(workItemId));
            await LoadAsync();
        }
        catch (Exception exception)
        {
            error = $"The Work Item could not be restored: {exception.Message}";
        }
        finally
        {
            restoringWorkItemId = null;
        }
    }
}
