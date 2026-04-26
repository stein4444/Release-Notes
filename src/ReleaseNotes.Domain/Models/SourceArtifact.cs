namespace ReleaseNotes.Domain.Models;

public sealed record SourceArtifact(
    string Id,
    string Title,
    string? Body,
    string Author,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<string> ChangedFiles,
    DateTimeOffset CreatedAt);
