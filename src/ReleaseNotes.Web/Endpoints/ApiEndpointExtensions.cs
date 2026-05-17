using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Persistence.Entities;
using ReleaseNotes.Infrastructure.Utilities;
using ReleaseNotes.Web.Models;
using static ReleaseNotes.Infrastructure.Persistence.ReleaseNoteJson;

namespace ReleaseNotes.Web.Endpoints;

public static class ApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapReleaseNotesApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", async (
            RegisterRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await auth.RegisterAsync(
                    request.Email,
                    request.Password,
                    request.DisplayName ?? string.Empty,
                    cancellationToken);
                return AuthResultToHttp(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).AllowAnonymous().DisableAntiforgery();

        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            IAuthService auth,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(request.Email, request.Password, cancellationToken);
            return AuthResultToHttp(result);
        }).AllowAnonymous().DisableAntiforgery();

        var api = app.MapGroup("/api").RequireAuthorization().DisableAntiforgery();

        api.MapPost("/release-notes/generate", async (
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
                    return Results.BadRequest(new { message = "BaseTag і TargetTag обов'язкові, або обидва *." });
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

        api.MapGet("/dashboard/repositories", async (
            ReleaseNotesDbContext db,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            const int maxCommitsPerRepo = 300;
            var userId = currentUser.GetRequiredUserId();

            var userPaths = await db.RepositoryConnections.AsNoTracking()
                .Where(c => c.OwnerUserId == userId)
                .Select(c => RepositoryPathNormalizer.Normalize(c.RepositoryPath))
                .ToListAsync(cancellationToken);
            var pathSet = userPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var documents = await db.Documents.AsNoTracking()
                .Where(d => d.OwnerUserId == userId)
                .ToListAsync(cancellationToken);

            var connections = await db.RepositoryConnections.AsNoTracking()
                .Where(c => c.OwnerUserId == userId)
                .ToListAsync(cancellationToken);

            var pathToDisplay = connections
                .GroupBy(c => RepositoryPathNormalizer.Normalize(c.RepositoryPath))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First().DisplayName);

            var byRepo = documents
                .Where(d => pathSet.Contains(RepositoryPathNormalizer.Normalize(d.Repository)))
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

        api.MapGet("/release-notes/{id:guid}", async (
            Guid id,
            IReleaseNotesRepository repository,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            var doc = await repository.GetDocumentAsync(id, currentUser.GetRequiredUserId(), cancellationToken);
            return doc is null ? Results.NotFound() : Results.Ok(doc);
        });

        api.MapGet("/releases/latest", async (
            int count,
            IReleaseNotesRepository repository,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            var safeCount = count <= 0 ? 10 : Math.Min(count, 50);
            var docs = await repository.GetLatestAsync(safeCount, currentUser.GetRequiredUserId(), cancellationToken);
            return Results.Ok(docs);
        });

        api.MapPost("/webhooks/github", async (
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
                return Results.BadRequest("baseTag and targetTag are required, or both *.");
            }

            var id = await useCase.ExecuteAsync(
                new GenerateReleaseNotesRequest(payload.RepositoryConnectionId, baseT, targetT, cancellationToken),
                cancellationToken);
            return Results.Ok(new { id });
        });

        api.MapGet("/repositories", async (
            ReleaseNotesDbContext db,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            var userId = currentUser.GetRequiredUserId();
            var repos = await db.RepositoryConnections.AsNoTracking()
                .Where(x => x.OwnerUserId == userId)
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

        api.MapPost("/repositories", async (
            RepositoryConnectionRequest request,
            ReleaseNotesDbContext db,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest("DisplayName, Provider and RepositoryPath are required.");
            }

            var userId = currentUser.GetRequiredUserId();
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
                OwnerUserId = userId,
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
                return Results.Conflict(new { message = "Такий репозиторій уже додано до вашого облікового запису." });
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

        api.MapPut("/repositories/{id:guid}", async (
            Guid id,
            RepositoryConnectionRequest request,
            ReleaseNotesDbContext db,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            var userId = currentUser.GetRequiredUserId();
            var entity = await db.RepositoryConnections.FirstOrDefaultAsync(
                x => x.Id == id && x.OwnerUserId == userId,
                cancellationToken);
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
                return Results.Conflict(new { message = "Такий провайдер і шлях уже є у вашому акаунті." });
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

        api.MapDelete("/repositories/{id:guid}", async (
            Guid id,
            ReleaseNotesDbContext db,
            ICurrentUserAccessor currentUser,
            CancellationToken cancellationToken) =>
        {
            var userId = currentUser.GetRequiredUserId();
            var entity = await db.RepositoryConnections.FirstOrDefaultAsync(
                x => x.Id == id && x.OwnerUserId == userId,
                cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            db.RepositoryConnections.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        api.MapGet("/integrations", async (ReleaseNotesDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.ServiceIntegrations.AsNoTracking()
                .OrderBy(x => x.DisplayName)
                .ToListAsync(cancellationToken);
            return Results.Ok(items);
        });

        api.MapPost("/integrations", async (
            ServiceIntegrationRequest request,
            ReleaseNotesDbContext db,
            CancellationToken cancellationToken) =>
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

        return app;
    }

    private static IResult AuthResultToHttp(AuthResult result)
    {
        if (!result.Success || result.Token is null || result.UserId is null)
        {
            return Results.BadRequest(new { message = result.ErrorMessage ?? "Auth failed." });
        }

        return Results.Ok(new AuthResponse(
            result.Token,
            result.UserId.Value,
            result.Email ?? string.Empty,
            result.DisplayName ?? string.Empty));
    }
}
