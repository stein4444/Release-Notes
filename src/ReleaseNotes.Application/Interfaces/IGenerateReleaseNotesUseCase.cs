using ReleaseNotes.Application.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IGenerateReleaseNotesUseCase
{
    Task<Guid> ExecuteAsync(GenerateReleaseNotesRequest request, CancellationToken cancellationToken);
}
