using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.IoC;
using Samwise.Infrastructure.Mcp.Endpoints;
using Samwise.Web.Components;
using Samwise.Web.Endpoints;
using Samwise.Web.Endpoints.ManualInput;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
InstallerDiscovery.RunInstallersFromAllReferencedAssemblies(
    builder.Services,
    builder.Configuration,
    builder.Environment.IsProduction(),
    "Samwise");
builder.Services.AddAllInitializers("Samwise");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
})
    .AllowAnonymous();
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapOwnerAccountEndpoints();
app.MapManualInputApi();
app.MapSamwiseMcp();

var initializers = app.Services.GetRequiredService<InitializerManager>();
await initializers.RunAllInitializersAsync(InitializerTrigger.OnStartup);
await app.StartAsync();
await initializers.RunAllInitializersAsync(InitializerTrigger.OnApplicationReady);
await app.WaitForShutdownAsync();
