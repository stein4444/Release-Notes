using ReleaseNotes.Domain.Enums;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Services;
using Xunit;

namespace ReleaseNotes.Tests.Unit;

public sealed class RuleEngineTests
{
    [Fact]
    public void Classify_WhenFixMessage_ReturnsFixCategory()
    {
        var engine = new RuleEngine();
        var artifact = new SourceArtifact("1", "fix: null ref in service", null, "dev", Array.Empty<string>(), Array.Empty<string>(), DateTimeOffset.UtcNow);

        var result = engine.Classify(artifact);

        Assert.Equal(ChangeCategory.Fix, result.Category);
    }
}
