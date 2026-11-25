using Samwise.Web.Components;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.WorkItems.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Web.Pages.WorkItems;

public partial class DiscardedWorkItems : AppComponentBase
{
    private IReadOnlyList<DomainWorkItem> workItems = [];
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

    private Task LoadAsync()
    {
        error = null;
        return ExecuteLoadAsync(token => WorkItemRepository.ListAsync(new DiscardedWorkItemsSpecification(), token),
            items => workItems = items);
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
