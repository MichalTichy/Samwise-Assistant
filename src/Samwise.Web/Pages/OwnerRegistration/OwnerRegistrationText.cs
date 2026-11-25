using System.Globalization;
using System.Resources;

namespace Samwise.Web.Pages.OwnerRegistration;

public static class OwnerRegistrationText
{
    private static readonly ResourceManager Resources = new(typeof(OwnerRegistrationText));
    public static string PageTitle => Resources.GetString(nameof(PageTitle), CultureInfo.CurrentUICulture)!;
    public static string Title => Resources.GetString(nameof(Title), CultureInfo.CurrentUICulture)!;
    public static string Introduction => Resources.GetString(nameof(Introduction), CultureInfo.CurrentUICulture)!;
    public static string Email => Resources.GetString(nameof(Email), CultureInfo.CurrentUICulture)!;
    public static string Password => Resources.GetString(nameof(Password), CultureInfo.CurrentUICulture)!;
    public static string ConfirmPassword => Resources.GetString(nameof(ConfirmPassword), CultureInfo.CurrentUICulture)!;
    public static string PasswordHint => Resources.GetString(nameof(PasswordHint), CultureInfo.CurrentUICulture)!;
    public static string CreateAccount => Resources.GetString(nameof(CreateAccount), CultureInfo.CurrentUICulture)!;
    public static string Unavailable => Resources.GetString(nameof(Unavailable), CultureInfo.CurrentUICulture)!;
    public static string SignIn => Resources.GetString(nameof(SignIn), CultureInfo.CurrentUICulture)!;
    public static string InvalidInput => Resources.GetString(nameof(InvalidInput), CultureInfo.CurrentUICulture)!;
    public static string EmailRequired => Resources.GetString(nameof(EmailRequired), CultureInfo.CurrentUICulture)!;
    public static string EmailInvalid => Resources.GetString(nameof(EmailInvalid), CultureInfo.CurrentUICulture)!;
    public static string PasswordRequired => Resources.GetString(nameof(PasswordRequired), CultureInfo.CurrentUICulture)!;
    public static string PasswordLength => Resources.GetString(nameof(PasswordLength), CultureInfo.CurrentUICulture)!;
    public static string PasswordsMustMatch => Resources.GetString(nameof(PasswordsMustMatch), CultureInfo.CurrentUICulture)!;
}
