namespace Samwise.Inputs.Todoist.Authentication;

public interface ITodoistCredentialProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
