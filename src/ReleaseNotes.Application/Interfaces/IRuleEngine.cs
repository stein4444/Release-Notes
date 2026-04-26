using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application.Interfaces;

public interface IRuleEngine
{
    ReleaseNoteEntry Classify(SourceArtifact artifact);
}
