using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Infrastructure.Initializer;

namespace Samwise.Infrastructure.Persistence.Identity;

public sealed class OwnerIdentityInitializer(
    ApplicationDbContext database,
    UserManager<ApplicationUser> userManager,
    IOptions<OwnerAccountOptions> options) : InitializerBase
{
    public override int Priority => int.MaxValue;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        await database.Database.MigrateAsync();

        var existingUsers = await userManager.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Take(2)
            .Select(user => new { user.Id, user.Email })
            .ToListAsync();

        if (existingUsers.Count > 1)
        {
            throw new InvalidOperationException(
                "Samwise is configured for one owner, but the Identity database contains multiple users.");
        }

        var ownerOptions = options.Value;
        var configuredEmail = ownerOptions.Email?.Trim();
        var initialPassword = ownerOptions.InitialPassword;

        if (existingUsers.Count == 1)
        {
            if (!string.IsNullOrWhiteSpace(configuredEmail) &&
                !string.Equals(existingUsers[0].Email, configuredEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "OwnerAccount:Email does not match the owner already stored in the Identity database.");
            }

            var existingOwner = await userManager.FindByIdAsync(existingUsers[0].Id)
                ?? throw new InvalidOperationException("The configured owner could not be loaded.");
            if (!await userManager.HasPasswordAsync(existingOwner))
            {
                EnsureInitialPasswordConfigured(initialPassword);
                var addPasswordResult = await userManager.AddPasswordAsync(existingOwner, initialPassword!);
                EnsureSucceeded(addPasswordResult, "Could not set the initial Samwise owner password");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(configuredEmail))
        {
            throw new InvalidOperationException(
                "No Samwise owner exists. Configure OwnerAccount:Email before the first startup.");
        }

        EnsureInitialPasswordConfigured(initialPassword);
        var owner = new ApplicationUser
        {
            UserName = configuredEmail,
            Email = configuredEmail,
            EmailConfirmed = true,
            LockoutEnabled = false
        };

        var result = await userManager.CreateAsync(owner, initialPassword!);
        EnsureSucceeded(result, "Could not create the Samwise owner account");
    }

    private static void EnsureInitialPasswordConfigured(string? initialPassword)
    {
        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            throw new InvalidOperationException(
                "No Samwise owner password exists. Configure OwnerAccount:InitialPassword before the first startup.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var descriptions = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"{message}: {descriptions}");
    }
}
