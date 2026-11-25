using Samwise.Web.Components;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Samwise.Inputs.Manual.Usecases;

namespace Samwise.Web.Pages;

public partial class Home : AppComponentBase
{
    private readonly ManualWorkItemInput input = new();

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private Task SubmitAsync() => ExecuteActionAsync(async () =>
    {
        await SendAsync(new SubmitManualInputUsecase(input.Text));
        Navigation.NavigateTo("/inbox?submitted=true");
    });

    private sealed class ManualWorkItemInput
    {
        [Required(ErrorMessage = "Enter some text first.")]
        [StringLength(20_000, MinimumLength = 3, ErrorMessage = "Use between 3 and 20,000 characters.")]
        public string Text { get; set; } = string.Empty;
    }
}
