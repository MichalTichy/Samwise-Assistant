using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Samwise.Infrastructure.Persistence.Identity;
using Samwise.Infrastructure.Persistence.Identity.Installers;
using Samwise.Infrastructure.Persistence.Identity.OwnerAccounts;

namespace Samwise.Tests.Identity;

public sealed class OwnerRegistrationTests : IAsyncLifetime
{
    private const string Token = "a-private-installation-token-with-at-least-32-characters";
    private const string Password = "a private owner password";
    private readonly string databaseName = "samwise_identity_test_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? provider;
    private string? connectionString;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SAMWISE_IDENTITY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) return;
        await using var connection = new NpgsqlConnection(configured);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(configured) { Database = databaseName }.ConnectionString;
        provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private ServiceProvider CreateProvider(string? email = null, string? initialPassword = null, bool registration = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:samwise"] = connectionString,
            ["OwnerAccount:RegistrationToken"] = registration ? Token : null,
            ["OwnerAccount:Email"] = email,
            ["OwnerAccount:InitialPassword"] = initialPassword
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        new IdentityInstaller().Install(services, configuration, false);
        return services.BuildServiceProvider();
    }

    private async Task<OwnerRegistrationResult> RegisterAsync(string email = "owner@example.com", string password = Password, string token = Token)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new RegisterOwnerUsecase(token, email, password));
    }

    [PostgreSqlIdentityFact]
    public async Task RegisteredOwnerCanUseOwnPasswordAndLinkIsConsumed()
    {
        Assert.Equal(OwnerRegistrationResult.Created, await RegisterAsync());
        await using var scope = provider!.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.False(await services.GetRequiredService<OwnerRegistrationAccess>().IsAvailableAsync(Token));
        var summary = await services.GetRequiredService<OwnerAccountReader>().ReadAsync();
        Assert.Equal("owner@example.com", summary!.Email);
        var user = await services.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(summary.Email);
        Assert.True(await services.GetRequiredService<UserManager<ApplicationUser>>().CheckPasswordAsync(user!, Password));
        Assert.NotEqual(Password, user!.PasswordHash);
        Assert.Equal(OwnerRegistrationResult.Unavailable, await RegisterAsync("other@example.com"));
    }

    [PostgreSqlIdentityFact]
    public async Task WrongTokenCreatesNothing()
    {
        Assert.Equal(OwnerRegistrationResult.Unavailable, await RegisterAsync(token: "incorrect"));
        await using var scope = provider!.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<OwnerAccountReader>().ReadAsync());
    }

    [PostgreSqlIdentityFact]
    public async Task InvalidEmailOrPasswordLeavesLinkUsable()
    {
        Assert.Equal(OwnerRegistrationResult.InvalidInput, await RegisterAsync(email: "invalid"));
        Assert.Equal(OwnerRegistrationResult.InvalidInput, await RegisterAsync(password: "short"));
        Assert.Equal(OwnerRegistrationResult.Created, await RegisterAsync());
    }

    [PostgreSqlIdentityFact]
    public async Task ConcurrentRegistrationCreatesOnlyOneOwner()
    {
        var results = await Task.WhenAll(RegisterAsync("first@example.com"), RegisterAsync("second@example.com"));
        Assert.Single(results, r => r == OwnerRegistrationResult.Created);
        Assert.Single(results, r => r == OwnerRegistrationResult.Unavailable);
        await using var scope = provider!.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [PostgreSqlIdentityFact]
    public async Task RestartPreservesIdentityWithoutConfiguredEmailAndKeepsRegistrationClosed()
    {
        Assert.Equal(OwnerRegistrationResult.Created, await RegisterAsync());
        await using var restarted = CreateProvider();
        await using var scope = restarted.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var initializer = ActivatorUtilities.CreateInstance<OwnerIdentityInitializer>(services);
        await initializer.InitializeAsync(new ConfigurationBuilder().Build());
        Assert.Equal("owner@example.com", (await services.GetRequiredService<OwnerAccountReader>().ReadAsync())!.Email);
        Assert.False(await services.GetRequiredService<OwnerRegistrationAccess>().IsAvailableAsync(Token));
    }

    [PostgreSqlIdentityFact]
    public async Task BootstrapStartupDoesNotSeedLegacyDefaults()
    {
        await using var bootstrap = CreateProvider("default@example.com", "a default initial password");
        await using var scope = bootstrap.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<OwnerIdentityInitializer>(scope.ServiceProvider)
            .InitializeAsync(new ConfigurationBuilder().Build());
        Assert.Null(await scope.ServiceProvider.GetRequiredService<OwnerAccountReader>().ReadAsync());
        Assert.True(await scope.ServiceProvider.GetRequiredService<OwnerRegistrationAccess>().IsAvailableAsync(Token));
    }

    [PostgreSqlIdentityFact]
    public async Task LegacyOwnerAndPasswordSurviveUpgrade()
    {
        await using (var legacy = CreateProvider("legacy@example.com", Password, registration: false))
        await using (var scope = legacy.CreateAsyncScope())
            await ActivatorUtilities.CreateInstance<OwnerIdentityInitializer>(scope.ServiceProvider)
                .InitializeAsync(new ConfigurationBuilder().Build());
        await using var upgraded = CreateProvider("legacy@example.com", "a different configured password");
        await using var upgradedScope = upgraded.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<OwnerIdentityInitializer>(upgradedScope.ServiceProvider)
            .InitializeAsync(new ConfigurationBuilder().Build());
        var users = upgradedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = await users.FindByEmailAsync("legacy@example.com");
        Assert.True(await users.CheckPasswordAsync(owner!, Password));
        Assert.False(await upgradedScope.ServiceProvider.GetRequiredService<OwnerRegistrationAccess>().IsAvailableAsync(Token));
    }

    public async Task DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        var configured = Environment.GetEnvironmentVariable("SAMWISE_IDENTITY_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(configured);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
