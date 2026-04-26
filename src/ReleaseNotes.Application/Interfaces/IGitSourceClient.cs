using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IGitSourceClient
{
    Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(
        string repository,
        string baseTag,
        string targetTag,
        CancellationToken cancellationToken);
}
