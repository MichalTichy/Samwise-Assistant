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

        if (existingUsers.Count == 1)
        {
            if (!string.IsNullOrWhiteSpace(ownerOptions.Email) &&
                !string.Equals(existingUsers[0].Email, ownerOptions.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "OwnerAccount:Email does not match the owner already stored in the Identity database.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(ownerOptions.Email))
        {
            throw new InvalidOperationException(
                "No Samwise owner exists. Configure OwnerAccount:Email through user secrets or " +
                "environment variables before the first startup.");
        }

        var email = ownerOptions.Email.Trim();
        var owner = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var result = await userManager.CreateAsync(owner);
        if (!result.Succeeded)
        {
            var descriptions = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not create the Samwise owner account: {descriptions}");
        }
    }
}
