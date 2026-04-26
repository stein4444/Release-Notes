namespace ReleaseNotes.Application.Models;

public sealed record GenerateReleaseNotesRequest(string Repository, string BaseTag, string TargetTag, CancellationToken CancellationToken);
