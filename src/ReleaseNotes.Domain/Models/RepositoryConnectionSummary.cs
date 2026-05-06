namespace ReleaseNotes.Domain.Models;

public sealed record RepositoryConnectionSummary(
    Guid Id,
    string Provider,
    string RepositoryPath,
    string? AccessToken,
    bool IsActive);
