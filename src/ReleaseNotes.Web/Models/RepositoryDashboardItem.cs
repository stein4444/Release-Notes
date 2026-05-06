namespace ReleaseNotes.Web.Models;

public sealed record RepositoryDashboardItem(
    string Repository,
    string? DisplayName,
    int DocumentsCount,
    int JobsCount,
    DateTimeOffset? LastGeneratedAt,
    string LastJobStatus);
