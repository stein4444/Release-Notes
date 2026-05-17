using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Persistence;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class RepositoryConnectionReader(ReleaseNotesDbContext dbContext) : IRepositoryConnectionReader
{
    public async Task<RepositoryConnectionSummary?> GetActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.RepositoryConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);

        return entity is null ? null : ToSummary(entity);
    }

    public async Task<RepositoryConnectionSummary?> GetActiveForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.RepositoryConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == userId && x.IsActive, cancellationToken);

        return entity is null ? null : ToSummary(entity);
    }

    public async Task<Guid?> GetOwnerUserIdAsync(Guid connectionId, CancellationToken cancellationToken) =>
        await dbContext.RepositoryConnections.AsNoTracking()
            .Where(x => x.Id == connectionId)
            .Select(x => (Guid?)x.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);

    private static RepositoryConnectionSummary ToSummary(Persistence.Entities.RepositoryConnectionEntity entity) =>
        new(entity.Id, entity.Provider, entity.RepositoryPath, entity.AccessToken, entity.IsActive);
}
