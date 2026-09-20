namespace Samwise.Inputs.Teams;

public interface ITeamsCredentialProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
