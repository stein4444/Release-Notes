using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Infrastructure;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Persistence.Entities;
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
builder.Services.AddScoped<IReleaseNotesAppApi>(sp =>
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
    var httpClient = new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
    return new ReleaseNotesAppApi(httpClient);
});

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
    try
    {
        if (request.RepositoryConnectionId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "RepositoryConnectionId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.BaseTag) || string.IsNullOrWhiteSpace(request.TargetTag))
        {
            return Results.BadRequest(new { message = "BaseTag and TargetTag are required." });
        }

        var id = await useCase.ExecuteAsync(
            new GenerateReleaseNotesRequest(request.RepositoryConnectionId, request.BaseTag.Trim(), request.TargetTag.Trim(), cancellationToken),
            cancellationToken);
        return Results.Accepted($"/api/release-notes/{id}", new { id });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/dashboard/repositories", async (ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    var documents = await db.Documents.AsNoTracking().ToListAsync(cancellationToken);
    var jobs = await db.Jobs.AsNoTracking().ToListAsync(cancellationToken);
    var connections = await db.RepositoryConnections.AsNoTracking().ToListAsync(cancellationToken);
    var pathToDisplay = connections
        .GroupBy(c => c.RepositoryPath)
        .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First().DisplayName);

    var byRepo = documents
        .GroupBy(x => x.Repository)
        .Select(g =>
        {
            var repoJobs = jobs.Where(j => j.Repository == g.Key).OrderByDescending(j => j.CreatedAt).ToList();
            var latestJob = repoJobs.FirstOrDefault();

            return new RepositoryDashboardItem(
                g.Key,
                pathToDisplay.GetValueOrDefault(g.Key),
                g.Count(),
                repoJobs.Count,
                g.Max(d => d.GeneratedAt),
                latestJob?.Status ?? "Unknown");
        })
        .OrderByDescending(x => x.LastGeneratedAt)
        .ToList();

    return Results.Ok(byRepo);
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
    if (payload.RepositoryConnectionId == Guid.Empty || string.IsNullOrWhiteSpace(payload.BaseTag) || string.IsNullOrWhiteSpace(payload.TargetTag))
    {
        return Results.BadRequest("RepositoryConnectionId, baseTag and targetTag are required.");
    }

    var id = await useCase.ExecuteAsync(new GenerateReleaseNotesRequest(payload.RepositoryConnectionId, payload.BaseTag.Trim(), payload.TargetTag.Trim(), cancellationToken), cancellationToken);
    return Results.Ok(new { id });
});

app.MapGet("/api/repositories", async (ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    var repos = await db.RepositoryConnections.AsNoTracking()
        .OrderBy(x => x.DisplayName)
        .Select(x => new
        {
            x.Id,
            x.DisplayName,
            x.Provider,
            x.RepositoryPath,
            x.IsActive,
            x.CreatedAt,
            x.UpdatedAt
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(repos);
});

app.MapPost("/api/repositories", async (RepositoryConnectionRequest request, ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.RepositoryPath))
    {
        return Results.BadRequest("DisplayName, Provider and RepositoryPath are required.");
    }

    var entity = new RepositoryConnectionEntity
    {
        Id = Guid.NewGuid(),
        DisplayName = request.DisplayName.Trim(),
        Provider = request.Provider.Trim().ToLowerInvariant(),
        RepositoryPath = request.RepositoryPath.Trim(),
        AccessToken = string.IsNullOrWhiteSpace(request.AccessToken) ? null : request.AccessToken.Trim(),
        IsActive = request.IsActive,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    db.RepositoryConnections.Add(entity);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/repositories/{entity.Id}", new
    {
        entity.Id,
        entity.DisplayName,
        entity.Provider,
        entity.RepositoryPath,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt
    });
});

app.MapPut("/api/repositories/{id:guid}", async (Guid id, RepositoryConnectionRequest request, ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    var entity = await db.RepositoryConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (entity is null)
    {
        return Results.NotFound();
    }

    entity.DisplayName = request.DisplayName.Trim();
    entity.Provider = request.Provider.Trim().ToLowerInvariant();
    entity.RepositoryPath = request.RepositoryPath.Trim();
    entity.AccessToken = string.IsNullOrWhiteSpace(request.AccessToken) ? null : request.AccessToken.Trim();
    entity.IsActive = request.IsActive;
    entity.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        entity.Id,
        entity.DisplayName,
        entity.Provider,
        entity.RepositoryPath,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt
    });
});

app.MapDelete("/api/repositories/{id:guid}", async (Guid id, ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    var entity = await db.RepositoryConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (entity is null)
    {
        return Results.NotFound();
    }

    db.RepositoryConnections.Remove(entity);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/integrations", async (ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    var items = await db.ServiceIntegrations.AsNoTracking()
        .OrderBy(x => x.DisplayName)
        .ToListAsync(cancellationToken);
    return Results.Ok(items);
});

app.MapPost("/api/integrations", async (ServiceIntegrationRequest request, ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.DisplayName))
    {
        return Results.BadRequest("Provider and DisplayName are required.");
    }

    var entity = new ServiceIntegrationEntity
    {
        Id = Guid.NewGuid(),
        Provider = request.Provider.Trim().ToLowerInvariant(),
        DisplayName = request.DisplayName.Trim(),
        SettingsJson = string.IsNullOrWhiteSpace(request.SettingsJson) ? "{}" : request.SettingsJson,
        IsEnabled = request.IsEnabled,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    db.ServiceIntegrations.Add(entity);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/integrations/{entity.Id}", entity);
});

app.Run();
