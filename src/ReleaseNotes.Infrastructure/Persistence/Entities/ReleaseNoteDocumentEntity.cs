namespace ReleaseNotes.Infrastructure.Persistence.Entities;

public sealed class ReleaseNoteDocumentEntity
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Repository { get; set; } = string.Empty;
    public string BaseTag { get; set; } = string.Empty;
    public string TargetTag { get; set; } = string.Empty;
    public string AiSummary { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; }
    public string EntriesJson { get; set; } = "[]";
}
