namespace ReleaseNotes.Web.Models;

public sealed record DashboardCommitItem(
    string SourceId,
    string Summary,
    DateTimeOffset? CommittedAt);

public sealed record RepositoryDashboardItem(
    string Repository,
    string? DisplayName,
    DateTimeOffset? LastGeneratedAt,
    string BaseTag,
    string TargetTag,
    IReadOnlyList<DashboardCommitItem> Commits);
