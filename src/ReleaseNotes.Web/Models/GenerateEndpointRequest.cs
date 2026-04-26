namespace ReleaseNotes.Web.Models;

public sealed record GenerateEndpointRequest(string Repository, string BaseTag, string TargetTag);
