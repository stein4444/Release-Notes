using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IRepositoryConnectionReader
{
    Task<RepositoryConnectionSummary?> GetActiveAsync(Guid id, CancellationToken cancellationToken);
}
