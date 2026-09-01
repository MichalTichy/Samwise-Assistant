using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Infrastructure.Persistence.Identity;
using Shared.Infrastructure.IoC;

namespace Samwise.Infrastructure.Persistence.Identity.Installers;

public sealed class IdentityInstaller : IInstaller
{
    public void Install(IServiceCollection services, IConfiguration configuration, bool isProduction)
    {
        var connectionString = configuration.GetConnectionString("samwise")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'samwise' was not configured.");
        var requireHttps = configuration.GetValue<bool>("Authentication:RequireHttps");

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme);

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                    IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
        services.AddDatabaseDeveloperPageExceptionFilter();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.SecurePolicy = requireHttps
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
        });

        services.Configure<OwnerAccountOptions>(configuration.GetSection(OwnerAccountOptions.SectionName));

        var dataProtectionPath = configuration["DataProtection:KeysPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".data", "data-protection");
        Directory.CreateDirectory(dataProtectionPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
            .SetApplicationName("Samwise");
    }
}
