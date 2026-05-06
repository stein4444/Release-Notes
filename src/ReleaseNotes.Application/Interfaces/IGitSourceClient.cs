using ReleaseNotes.Application.Models;
using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IGitSourceClient
{
    Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(
        GitHubCompareRequest request,
        CancellationToken cancellationToken);
}
