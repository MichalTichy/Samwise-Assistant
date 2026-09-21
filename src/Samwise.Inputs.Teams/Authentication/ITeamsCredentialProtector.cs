namespace Samwise.Inputs.Teams.Authentication;

public interface ITeamsCredentialProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
