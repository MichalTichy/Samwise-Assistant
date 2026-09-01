using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Samwise.Infrastructure.Persistence.Identity;

namespace Samwise.Web.Components.Pages;

public partial class ChangePassword
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private ILogger<ChangePassword> Logger { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private PasswordInput Input { get; set; } = default!;

    private string? Message { get; set; }
    private bool Succeeded { get; set; }

    protected override void OnInitialized() => Input ??= new PasswordInput();

    private async Task ChangePasswordAsync()
    {
        var owner = await UserManager.GetUserAsync(HttpContext.User);
        if (owner is null)
        {
            Message = "The owner account could not be loaded.";
            return;
        }

        var result = await UserManager.ChangePasswordAsync(owner, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            Message = string.Join(" ", result.Errors.Select(error => error.Description));
            return;
        }

        await SignInManager.RefreshSignInAsync(owner);
        Logger.LogInformation("The Samwise owner changed the password.");
        Input = new PasswordInput();
        Succeeded = true;
        Message = "Password changed.";
    }

    private sealed class PasswordInput
    {
        [Required]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 10)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(NewPassword), ErrorMessage = "The new passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
