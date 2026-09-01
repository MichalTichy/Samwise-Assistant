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
    private string description = string.Empty;
    private bool isEnabled = true;
    private IReadOnlyList<string> errors = [];
    private string? saveMessage;
    private bool saving;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<Playbook> PlaybookRepository { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        playbooks = await PlaybookRepository.ListAsync(new PlaybooksSpecification());
    }

    private void NewPlaybook()
    {
        selectedId = null;
        name = string.Empty;
        description = string.Empty;
        isEnabled = true;
        errors = [];
        saveMessage = null;
    }

    private void Select(Playbook playbook)
    {
        selectedId = playbook.Id;
        name = playbook.Name;
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
