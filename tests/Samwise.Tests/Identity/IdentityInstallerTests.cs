using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Samwise.Infrastructure.Persistence.Identity.Installers;

namespace Samwise.Tests.Identity;

public sealed class IdentityInstallerTests
{
    [Fact]
    public void ProductionCookiesRequireHttpsByDefault()
    {
        var options = GetCookieOptions(requireHttps: null);

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void HttpsRequirementCanBeDisabledForDirectHttpDeployment()
    {
        var options = GetCookieOptions(requireHttps: false);

        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
    }

    private static CookieAuthenticationOptions GetCookieOptions(bool? requireHttps)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:samwise"] = "Host=localhost;Database=samwise;Username=samwise;Password=samwise"
        };
        if (requireHttps.HasValue)
        {
            values["Authentication:RequireHttps"] = requireHttps.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        new IdentityInstaller().Install(services, configuration, isProduction: true);

        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
    }
}
