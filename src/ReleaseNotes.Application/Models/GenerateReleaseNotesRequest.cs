namespace ReleaseNotes.Application.Models;

public sealed record GenerateReleaseNotesRequest(
    Guid RepositoryConnectionId,
    string BaseTag,
    string TargetTag,
    CancellationToken CancellationToken,
    Guid? ActingUserId = null);
