namespace ReleaseNotes.Domain.Models;

public sealed class ReleaseNoteDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Repository { get; init; } = string.Empty;
    public string BaseTag { get; init; } = string.Empty;
    public string TargetTag { get; init; } = string.Empty;
    public string AiSummary { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyCollection<ReleaseNoteEntry> Entries { get; init; } = Array.Empty<ReleaseNoteEntry>();
}
