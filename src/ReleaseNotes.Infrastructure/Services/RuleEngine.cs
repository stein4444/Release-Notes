using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Domain.Enums;
using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class RuleEngine : IRuleEngine
{
    public ReleaseNoteEntry Classify(SourceArtifact artifact)
    {
        var text = $"{artifact.Title} {artifact.Body}".ToLowerInvariant();
        var category = ChangeCategory.Other;
        var breaking = false;

        if (text.Contains("breaking") || text.Contains("!:"))
        {
            category = ChangeCategory.BreakingChange;
            breaking = true;
        }
        else if (text.Contains("feat"))
        {
            category = ChangeCategory.Feature;
        }
        else if (text.Contains("fix"))
        {
            category = ChangeCategory.Fix;
        }
        else if (text.Contains("deps") || text.Contains("dependency"))
        {
            category = ChangeCategory.Dependency;
        }
        else if (text.Contains("improve") || text.Contains("refactor"))
        {
            category = ChangeCategory.Improvement;
        }

        return new ReleaseNoteEntry(artifact.Id, category, artifact.Title, breaking, 0.7d, artifact.CreatedAt);
    }
}
