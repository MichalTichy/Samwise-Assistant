using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Manual.Usecases;

namespace Samwise.Web.Components.Pages;

public partial class Home
{
    private readonly ManualWorkItemInput input = new();
    private bool submitting;
    private string? error;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private async Task SubmitAsync()
    {
        submitting = true;
        error = null;
        try
        {
            await Sender.Send(new SubmitManualInputUsecase(input.Text));
            Navigation.NavigateTo("/inbox");
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }
        finally
        {
            submitting = false;
        }
    }

    private sealed class ManualWorkItemInput
    {
        [Required(ErrorMessage = "Enter some text first.")]
        [StringLength(20_000, MinimumLength = 3, ErrorMessage = "Use between 3 and 20,000 characters.")]
        public string Text { get; set; } = string.Empty;
    }
}
