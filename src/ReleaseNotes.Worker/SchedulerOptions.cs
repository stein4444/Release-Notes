namespace ReleaseNotes.Worker;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";
    public bool Enabled { get; set; }
    public Guid RepositoryConnectionId { get; set; }
    public string BaseTag { get; set; } = string.Empty;
    public string TargetTag { get; set; } = string.Empty;
}
