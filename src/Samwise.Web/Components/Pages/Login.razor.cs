using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.Persistence.Identity;

namespace Samwise.Web.Components.Pages;

public partial class Login
{
    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IOptions<OwnerAccountOptions> OwnerOptions { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILogger<Login> Logger { get; set; } = default!;

    [SupplyParameterFromForm]
    private LoginInput Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    private string OwnerEmail => OwnerOptions.Value.Email ?? "owner";
    private string? Error { get; set; }

    protected override void OnInitialized() => Input ??= new LoginInput();

    private async Task SignInAsync()
    {
        var email = OwnerOptions.Value.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            Error = "The owner account is not configured.";
            return;
        }

        var result = await SignInManager.PasswordSignInAsync(
            email,
            Input.Password,
            isPersistent: true,
            lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            Error = "Incorrect password.";
            return;
        }

        Logger.LogInformation("The Samwise owner signed in.");
        Navigation.NavigateTo(GetSafeReturnUrl(), forceLoad: true);
    }

    private string GetSafeReturnUrl()
    {
        if (string.IsNullOrWhiteSpace(ReturnUrl) ||
            !ReturnUrl.StartsWith('/') ||
            ReturnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return ReturnUrl;
    }

    private sealed class LoginInput
    {
        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
