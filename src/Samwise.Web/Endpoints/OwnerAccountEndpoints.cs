using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Samwise.Infrastructure.Persistence.Identity;

namespace Samwise.Web.Endpoints;

internal static class OwnerAccountEndpoints
{
    public static IEndpointConventionBuilder MapOwnerAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/logout", async (
                HttpContext context,
                IAntiforgery antiforgery,
                SignInManager<ApplicationUser> signInManager) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                await signInManager.SignOutAsync();
                return Results.Redirect("/login");
            })
            .RequireAuthorization();
    }
}
