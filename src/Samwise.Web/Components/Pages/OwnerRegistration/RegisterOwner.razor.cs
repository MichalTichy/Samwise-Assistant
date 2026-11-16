using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Samwise.Infrastructure.Persistence.Identity;
using Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

namespace Samwise.Web.Components.Pages.OwnerRegistration;

public partial class RegisterOwner
{
    [Inject] private OwnerRegistrationAccess Access { get; set; } = default!;
    [Inject] private ISender Sender { get; set; } = default!;
    [Inject] private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;
    [SupplyParameterFromQuery(Name = "token")] private string? Token { get; set; }
    [SupplyParameterFromForm] private RegistrationInput Input { get; set; } = default!;
    private bool Available { get; set; }
    private string? Error { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();
        if (HttpMethods.IsGet(HttpContext.Request.Method))
            Input.Token = Token ?? string.Empty;
        Available = await Access.IsAvailableAsync(Input.Token, HttpContext.RequestAborted);
        if (!Available)
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private void ClearPasswords() => Input.Password = Input.ConfirmPassword = string.Empty;

    private async Task RegisterAsync()
    {
        if (!Available) return;
        var result = await Sender.Send(new RegisterOwnerUsecase(Input.Token, Input.Email, Input.Password), HttpContext.RequestAborted);
        if (result == OwnerRegistrationResult.Created)
        {
            var login = await SignInManager.PasswordSignInAsync(Input.Email.Trim(), Input.Password, true, false);
            Input.Password = Input.ConfirmPassword = string.Empty;
            Navigation.NavigateTo(login.Succeeded ? "/" : "/login", forceLoad: true);
            return;
        }
        Input.Password = Input.ConfirmPassword = string.Empty;
        Available = result != OwnerRegistrationResult.Unavailable;
        Error = OwnerRegistrationText.InvalidInput;
        if (!Available) HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private sealed class RegistrationInput
    {
        public string Token { get; set; } = string.Empty;
        [Required(ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.EmailRequired))]
        [EmailAddress(ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.EmailInvalid))]
        [StringLength(254, ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.EmailInvalid))]
        public string Email { get; set; } = string.Empty;
        [Required(ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.PasswordRequired))]
        [StringLength(128, MinimumLength = 10, ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.PasswordLength))]
        public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password), ErrorMessageResourceType = typeof(OwnerRegistrationText), ErrorMessageResourceName = nameof(OwnerRegistrationText.PasswordsMustMatch))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
