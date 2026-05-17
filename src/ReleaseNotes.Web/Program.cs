using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Infrastructure;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Web;
using ReleaseNotes.Web.Endpoints;
using ReleaseNotes.Web.Hubs;
using ReleaseNotes.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IProgressNotifier, SignalRProgressNotifier>();
builder.Services.AddScoped<IReleaseNotesAppApi>(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    var handler = sp.GetRequiredService<BearerTokenHandler>();
    handler.InnerHandler = new HttpClientHandler();
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri(navigationManager.BaseUri) };
    return new ReleaseNotesAppApi(httpClient, sp.GetRequiredService<AuthSession>());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReleaseNotesDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseAuthSchemaPatcher.ApplyAsync(db);
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHub<ReleaseProgressHub>("/hubs/release-progress");
app.MapReleaseNotesApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
