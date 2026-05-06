namespace ReleaseNotes.Web.Models;

public sealed record RepositoryConnectionRequest(
    string DisplayName,
    string Provider,
    string RepositoryPath,
    string? AccessToken,
    bool IsActive);
