using ReleaseNotes.Domain.Enums;

namespace ReleaseNotes.Domain.Models;

public sealed record ReleaseNoteEntry(
    string SourceId,
    ChangeCategory Category,
    string Summary,
    bool IsBreakingChange,
    double Confidence,
    DateTimeOffset CommittedAt = default);
