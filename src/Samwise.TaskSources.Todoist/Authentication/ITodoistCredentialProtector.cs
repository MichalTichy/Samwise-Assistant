namespace Samwise.TaskSources.Todoist.Authentication;

public interface ITodoistCredentialProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
