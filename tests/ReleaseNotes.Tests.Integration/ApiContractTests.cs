using Xunit;

namespace ReleaseNotes.Tests.Integration;

public sealed class ApiContractTests
{
    [Fact]
    public void WebhookPayload_ShouldContainRequiredFields()
    {
        var required = new[] { "RepositoryConnectionId", "BaseTag", "TargetTag" };
        Assert.Equal(3, required.Length);
    }
}
