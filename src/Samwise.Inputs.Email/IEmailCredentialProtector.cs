namespace Samwise.Inputs.Email;

public interface IEmailCredentialProtector
{
    string Protect(string password);
    string Unprotect(string protectedPassword);
}
