namespace ReleaseNotes.Domain.Models;

public sealed class ReleaseNoteJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Repository { get; init; } = string.Empty;
    public string BaseTag { get; init; } = string.Empty;
    public string TargetTag { get; init; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
