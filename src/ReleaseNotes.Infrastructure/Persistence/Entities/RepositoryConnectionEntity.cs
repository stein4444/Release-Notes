namespace ReleaseNotes.Infrastructure.Persistence.Entities;

public sealed class RepositoryConnectionEntity
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = "github";
    public string RepositoryPath { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
