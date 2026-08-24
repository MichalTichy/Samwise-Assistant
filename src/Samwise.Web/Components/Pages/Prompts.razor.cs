using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Prompts;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;

namespace Samwise.Web.Components.Pages;

public partial class Prompts
{
    private IReadOnlyList<PromptTemplateEditor> templates = [];
    private PromptTemplateEditor? selected;
    private string content = string.Empty;
    private IReadOnlyList<string> errors = [];
    private string? saveMessage;
    private bool saving;

    [Inject]
    private ISender Sender { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        templates = await Sender.Send(new GetPromptTemplatesUsecase());
        Select(templates.FirstOrDefault(template => template.Stage == PromptStage.Planning) ?? templates.FirstOrDefault());
    }

    private void Select(PromptTemplateEditor? template)
    {
        selected = template;
        content = template?.Content ?? string.Empty;
        errors = [];
        saveMessage = null;
    }

    private async Task SaveAsync()
    {
        if (selected is null)
        {
            return;
        }

        saving = true;
        saveMessage = null;
        try
        {
            var selectedStage = selected.Stage;
            var result = await Sender.Send(new SavePromptTemplateUsecase(selectedStage, content));
            errors = result.Errors;
            saveMessage = result.Succeeded ? "Saved" : null;
            if (result.Succeeded)
            {
                templates = await Sender.Send(new GetPromptTemplatesUsecase());
                Select(templates.Single(template => template.Stage == selectedStage));
                saveMessage = "Saved";
            }
        }
        finally
        {
            saving = false;
        }
    }

    private static string DescribeStage(PromptStage stage) => stage switch
    {
        PromptStage.Intake => "Classify every input",
        PromptStage.Planning => "Create approval plan",
        PromptStage.Execution => "Run approved plan",
        _ => stage.ToString()
    };
}
