namespace ReleaseNotes.Web.Models;

public sealed record GenerateEndpointRequest(Guid RepositoryConnectionId, string BaseTag, string TargetTag);
