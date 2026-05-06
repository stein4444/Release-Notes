namespace ReleaseNotes.Infrastructure.Persistence.Entities;

public sealed class ServiceIntegrationEntity
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
