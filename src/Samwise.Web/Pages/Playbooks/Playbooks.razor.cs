using Samwise.Web.Components;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Playbooks;
using Samwise.Logic.Playbooks.Specifications;
using Samwise.Logic.Playbooks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Playbooks;

public partial class Playbooks : AppComponentBase
{
    private IReadOnlyList<Playbook> playbooks = [];
    private string? selectedId;
    private string name = string.Empty;
    private string applicability = string.Empty;
    private string description = string.Empty;
    private bool isEnabled = true;
    private bool suitableForAutomaticApproval;
    private IReadOnlyList<string> errors = [];
    private string? saveMessage;
    private bool saving;
    private bool confirmingDelete;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<Playbook> PlaybookRepository { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadAsync();
            StateHasChanged();
        }
    }

    private Task LoadAsync() =>
        ExecuteLoadAsync(token => PlaybookRepository.ListAsync(new PlaybooksSpecification(), token),
            items => playbooks = items);

    private void NewPlaybook()
    {
        confirmingDelete = false;
        selectedId = null;
        name = string.Empty;
        applicability = string.Empty;
        description = string.Empty;
        isEnabled = true;
        suitableForAutomaticApproval = false;
        errors = [];
        saveMessage = null;
    }

    private void Select(Playbook playbook)
    {
        confirmingDelete = false;
        selectedId = playbook.Id;
        name = playbook.Name;
        applicability = playbook.Applicability;
        description = playbook.Description;
        isEnabled = playbook.IsEnabled;
        suitableForAutomaticApproval = playbook.SuitableForAutomaticApproval;
        errors = [];
        saveMessage = null;
    }

    private async Task DeleteAsync()
    {
        if (selectedId is null || saving)
        {
            return;
        }

        saving = true;
        try
        {
            await Sender.Send(new DeletePlaybookUsecase(selectedId));
            NewPlaybook();
            await LoadAsync();
            saveMessage = "Deleted";
        }
        finally
        {
            saving = false;
        }
    }

    private async Task SaveAsync()
    {
        confirmingDelete = false;
        saving = true;
        saveMessage = null;
        try
        {
            var result = await Sender.Send(new SavePlaybookUsecase(
                selectedId,
                name,
                applicability,
                description,
                isEnabled,
                suitableForAutomaticApproval));
            errors = result.Errors;
            if (!result.Succeeded)
            {
                return;
            }

            await LoadAsync();
            var saved = playbooks.Single(playbook => playbook.Id == result.PlaybookId);
            Select(saved);
            saveMessage = "Saved";
        }
        finally
        {
            saving = false;
        }
    }
}
