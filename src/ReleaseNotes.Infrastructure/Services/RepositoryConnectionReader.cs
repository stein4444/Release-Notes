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

        if (entity is null)
        {
            return null;
        }

        return new RepositoryConnectionSummary(
            entity.Id,
            entity.Provider,
            entity.RepositoryPath,
            entity.AccessToken,
            entity.IsActive);
    }
}
