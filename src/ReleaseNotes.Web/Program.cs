using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Infrastructure;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Persistence.Entities;
using static ReleaseNotes.Infrastructure.Persistence.ReleaseNoteJson;
using ReleaseNotes.Infrastructure.Utilities;
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

// Minimal APIs must be registered before MapRazorComponents so /api/* is not swallowed by Blazor routing.
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

        var baseT = request.BaseTag?.Trim() ?? string.Empty;
        var targetT = request.TargetTag?.Trim() ?? string.Empty;
        if (!GitIngestMode.IsFullRepositoryHistory(baseT, targetT)
            && (string.IsNullOrWhiteSpace(baseT) || string.IsNullOrWhiteSpace(targetT)))
        {
            return Results.BadRequest(new { message = "BaseTag і TargetTag обов'язкові, або обидва вкажіть як * для повного збору комітів." });
        }

        var id = await useCase.ExecuteAsync(
            new GenerateReleaseNotesRequest(request.RepositoryConnectionId, baseT, targetT, cancellationToken),
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
    const int maxCommitsPerRepo = 300;

    var documents = await db.Documents.AsNoTracking().ToListAsync(cancellationToken);
    var connections = await db.RepositoryConnections.AsNoTracking().ToListAsync(cancellationToken);
    var pathToDisplay = connections
        .GroupBy(c => RepositoryPathNormalizer.Normalize(c.RepositoryPath))
        .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First().DisplayName);

    var byRepo = documents
        .GroupBy(x => RepositoryPathNormalizer.Normalize(x.Repository))
        .Select(g =>
        {
            var latestDoc = g.OrderByDescending(d => d.GeneratedAt).First().ToModel();
            var commits = latestDoc.Entries
                .OrderByDescending(e => IsValidCommitDate(e.CommittedAt) ? e.CommittedAt : latestDoc.GeneratedAt)
                .Take(maxCommitsPerRepo)
                .Select(e => new DashboardCommitItem(
                    e.SourceId,
                    e.Summary,
                    IsValidCommitDate(e.CommittedAt) ? e.CommittedAt : null))
                .ToList();

            return new RepositoryDashboardItem(
                g.Key,
                pathToDisplay.GetValueOrDefault(g.Key),
                latestDoc.GeneratedAt,
                latestDoc.BaseTag,
                latestDoc.TargetTag,
                commits);
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
    if (payload.RepositoryConnectionId == Guid.Empty)
    {
        return Results.BadRequest("RepositoryConnectionId is required.");
    }

    var baseT = payload.BaseTag?.Trim() ?? string.Empty;
    var targetT = payload.TargetTag?.Trim() ?? string.Empty;
    if (!GitIngestMode.IsFullRepositoryHistory(baseT, targetT)
        && (string.IsNullOrWhiteSpace(baseT) || string.IsNullOrWhiteSpace(targetT)))
    {
        return Results.BadRequest("baseTag and targetTag are required, or both set to * for full history.");
    }

    var id = await useCase.ExecuteAsync(new GenerateReleaseNotesRequest(payload.RepositoryConnectionId, baseT, targetT, cancellationToken), cancellationToken);
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

    var provider = request.Provider.Trim().ToLowerInvariant();
    var repoPath = request.RepositoryPath.Trim();
    if (string.Equals(provider, "github", StringComparison.Ordinal))
    {
        try
        {
            repoPath = RepositoryPathNormalizer.Normalize(repoPath);
            RepositoryPathNormalizer.ParseOwnerRepo(repoPath);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    var entity = new RepositoryConnectionEntity
    {
        Id = Guid.NewGuid(),
        DisplayName = request.DisplayName.Trim(),
        Provider = provider,
        RepositoryPath = repoPath,
        AccessToken = string.IsNullOrWhiteSpace(request.AccessToken) ? null : request.AccessToken.Trim(),
        IsActive = request.IsActive,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    db.RepositoryConnections.Add(entity);
    try
    {
        await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
    {
        return Results.Conflict(new { message = "Репозиторій з таким провайдером і Repository Path уже є в базі. Відредагуйте існуючий запис або видаліть дублікат." });
    }

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

    var provider = request.Provider.Trim().ToLowerInvariant();
    var repoPath = request.RepositoryPath.Trim();
    if (string.Equals(provider, "github", StringComparison.Ordinal))
    {
        try
        {
            repoPath = RepositoryPathNormalizer.Normalize(repoPath);
            RepositoryPathNormalizer.ParseOwnerRepo(repoPath);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    entity.DisplayName = request.DisplayName.Trim();
    entity.Provider = provider;
    entity.RepositoryPath = repoPath;
    entity.AccessToken = string.IsNullOrWhiteSpace(request.AccessToken) ? null : request.AccessToken.Trim();
    entity.IsActive = request.IsActive;
    entity.UpdatedAt = DateTimeOffset.UtcNow;

    try
    {
        await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
    {
        return Results.Conflict(new { message = "Такий провайдер і Repository Path уже зайняті іншим записом." });
    }

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

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
