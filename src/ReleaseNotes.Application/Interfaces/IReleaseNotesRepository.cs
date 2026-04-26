using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IReleaseNotesRepository
{
    Task SaveJobAsync(ReleaseNoteJob job, CancellationToken cancellationToken);
    Task UpdateJobAsync(ReleaseNoteJob job, CancellationToken cancellationToken);
    Task SaveDocumentAsync(ReleaseNoteDocument document, CancellationToken cancellationToken);
    Task<ReleaseNoteDocument?> GetDocumentAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ReleaseNoteDocument>> GetLatestAsync(int count, CancellationToken cancellationToken);
}
