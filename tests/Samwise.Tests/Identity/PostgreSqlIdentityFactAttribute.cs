namespace Samwise.Tests.Identity;

public sealed class PostgreSqlIdentityFactAttribute : FactAttribute
{
    public PostgreSqlIdentityFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAMWISE_IDENTITY_TEST_CONNECTION")))
            Skip = "Set SAMWISE_IDENTITY_TEST_CONNECTION to run disposable PostgreSQL identity tests.";
    }
}
