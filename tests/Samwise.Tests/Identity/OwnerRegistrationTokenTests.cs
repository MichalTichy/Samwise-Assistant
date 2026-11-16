using Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

namespace Samwise.Tests.Identity;

public sealed class OwnerRegistrationTokenTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("short", "short", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    public void RequiresTheDedicatedLongToken(string? configured, string? supplied, bool expected) =>
        Assert.Equal(expected, OwnerRegistrationAccess.MatchesToken(configured, supplied));
}
