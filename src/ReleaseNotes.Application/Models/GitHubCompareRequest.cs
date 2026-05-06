namespace ReleaseNotes.Application.Models;

/// <summary>
/// Parameters for fetching commits between tags. CredentialScopeId isolates memory cache per connection.
/// </summary>
public sealed record GitHubCompareRequest(
    string RepositoryPath,
    string BaseTag,
    string TargetTag,
    string? AccessToken,
    Guid CredentialScopeId);
