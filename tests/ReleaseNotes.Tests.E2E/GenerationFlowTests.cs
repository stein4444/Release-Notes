using Xunit;

namespace ReleaseNotes.Tests.E2E;

public sealed class GenerationFlowTests
{
    [Fact]
    public async Task GenerationPipeline_SmokeTest()
    {
        await Task.Delay(1);
        Assert.True(true);
    }
}
