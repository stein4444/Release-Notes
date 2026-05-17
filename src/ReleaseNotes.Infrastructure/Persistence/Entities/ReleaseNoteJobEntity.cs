namespace ReleaseNotes.Infrastructure.Persistence.Entities;

public sealed class ReleaseNoteJobEntity
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Repository { get; set; } = string.Empty;
    public string BaseTag { get; set; } = string.Empty;
    public string TargetTag { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
