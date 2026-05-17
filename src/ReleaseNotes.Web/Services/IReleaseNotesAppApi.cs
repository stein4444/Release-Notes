using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Web.Services;

public interface IReleaseNotesAppApi
{
    Task<IReadOnlyList<RepositoryListDto>> GetRepositoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardRowDto>> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<GenerateNotesResult> GenerateReleaseNotesAsync(
        Guid repositoryConnectionId,
        string baseTag,
        string targetTag,
        CancellationToken cancellationToken = default);

    Task<ReleaseNoteDocument?> GetReleaseNoteDocumentAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveRepositoryAsync(RepositoryUpsertDto dto, CancellationToken cancellationToken = default);

    Task DeleteRepositoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveIntegrationAsync(IntegrationUpsertDto dto, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationListDto>> GetIntegrationsAsync(CancellationToken cancellationToken = default);
}

public sealed record RepositoryListDto(
    Guid Id,
    string DisplayName,
    string Provider,
    string RepositoryPath,
    bool IsActive);

public sealed record DashboardCommitDto(
    string SourceId,
    string Summary,
    DateTimeOffset? CommittedAt);

public sealed record DashboardRowDto(
    string Repository,
    string? DisplayName,
    DateTimeOffset? LastGeneratedAt,
    string BaseTag,
    string TargetTag,
    IReadOnlyList<DashboardCommitDto>? Commits)
{
    public IReadOnlyList<DashboardCommitDto> CommitList => Commits ?? [];
}

public sealed record GenerateNotesResult(bool Success, Guid? DocumentId, string Message);

public sealed record RepositoryUpsertDto(
    Guid? Id,
    string DisplayName,
    string Provider,
    string RepositoryPath,
    string? AccessToken,
    bool IsActive);

public sealed record IntegrationUpsertDto(
    string Provider,
    string DisplayName,
    string SettingsJson,
    bool IsEnabled);

public sealed record IntegrationListDto(Guid Id, string Provider, string DisplayName, bool IsEnabled);
