using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Playbooks;
using Samwise.Logic.Playbooks.Specifications;
using Samwise.Logic.Playbooks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class Playbooks
{
    private IReadOnlyList<Playbook> playbooks = [];
    private string? selectedId;
    private string name = string.Empty;
    private string applicability = string.Empty;
    private string description = string.Empty;
    private bool isEnabled = true;
    private IReadOnlyList<string> errors = [];
    private string? saveMessage;
    private bool saving;
    private bool loading = true;

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

    private async Task LoadAsync()
    {
        loading = true;
        try
        {
            playbooks = await PlaybookRepository.ListAsync(new PlaybooksSpecification());
        }
        finally
        {
            loading = false;
        }
    }

    private void NewPlaybook()
    {
        selectedId = null;
        name = string.Empty;
        applicability = string.Empty;
        description = string.Empty;
        isEnabled = true;
        errors = [];
        saveMessage = null;
    }

    private void Select(Playbook playbook)
    {
        selectedId = playbook.Id;
        name = playbook.Name;
        applicability = playbook.Applicability;
        description = playbook.Description;
        isEnabled = playbook.IsEnabled;
        errors = [];
        saveMessage = null;
    }

    private async Task SaveAsync()
    {
        saving = true;
        saveMessage = null;
        try
        {
            var result = await Sender.Send(new SavePlaybookUsecase(
                selectedId,
                name,
                applicability,
                description,
                isEnabled));
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
