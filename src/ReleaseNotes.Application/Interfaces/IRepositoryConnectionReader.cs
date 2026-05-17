using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IRepositoryConnectionReader
{
    Task<RepositoryConnectionSummary?> GetActiveAsync(Guid id, CancellationToken cancellationToken);

    Task<RepositoryConnectionSummary?> GetActiveForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    Task<Guid?> GetOwnerUserIdAsync(Guid connectionId, CancellationToken cancellationToken);
}
