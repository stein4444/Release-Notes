using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Infrastructure;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Web.Hubs;
using ReleaseNotes.Web;
using ReleaseNotes.Web.Models;
using ReleaseNotes.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IProgressNotifier, SignalRProgressNotifier>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReleaseNotesDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapHub<ReleaseProgressHub>("/hubs/release-progress");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapPost("/api/release-notes/generate", async (
    GenerateEndpointRequest request,
    IGenerateReleaseNotesUseCase useCase,
    CancellationToken cancellationToken) =>
{
    var id = await useCase.ExecuteAsync(
        new GenerateReleaseNotesRequest(request.Repository, request.BaseTag, request.TargetTag, cancellationToken),
        cancellationToken);
    return Results.Accepted($"/api/release-notes/{id}", new { id });
});

app.MapGet("/api/release-notes/{id:guid}", async (Guid id, IReleaseNotesRepository repository, CancellationToken cancellationToken) =>
{
    var doc = await repository.GetDocumentAsync(id, cancellationToken);
    return doc is null ? Results.NotFound() : Results.Ok(doc);
});

app.MapGet("/api/releases/latest", async (int count, IReleaseNotesRepository repository, CancellationToken cancellationToken) =>
{
    var safeCount = count <= 0 ? 10 : Math.Min(count, 50);
    var docs = await repository.GetLatestAsync(safeCount, cancellationToken);
    return Results.Ok(docs);
});

app.MapPost("/api/webhooks/github", async Task<IResult> (
    GithubWebhookPayload payload,
    IGenerateReleaseNotesUseCase useCase,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(payload.Repository) || string.IsNullOrWhiteSpace(payload.BaseTag) || string.IsNullOrWhiteSpace(payload.TargetTag))
    {
        return Results.BadRequest("Repository, baseTag and targetTag are required.");
    }

    var id = await useCase.ExecuteAsync(new GenerateReleaseNotesRequest(payload.Repository, payload.BaseTag, payload.TargetTag, cancellationToken), cancellationToken);
    return Results.Ok(new { id });
});

app.Run();
