using Microsoft.AspNetCore.Components;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Web.Services;

namespace ReleaseNotes.Web.Components.Features;

public partial class ReleaseNotesGenerator
{
    private const int MaxCommitsInUi = 2_000;

    [Inject] public IReleaseNotesAppApi Api { get; set; } = default!;

    private GenerateModel _model = new();

    private List<RepositoryListDto> _repositories = [];

    private IEnumerable<RepositoryListDto> ActiveGithubRepositories =>
        _repositories.Where(r => r.IsActive && string.Equals(r.Provider, "github", StringComparison.OrdinalIgnoreCase));

    private string _status = string.Empty;
    private bool IsGenerating { get; set; }
    private ReleaseNoteDocument? _document;
    private IReadOnlyList<ReleaseNoteEntry> _commitsForUi = [];
    private int _commitsTotal;
    private bool _commitsTruncated;

    private string StatusClass => _status.Contains("помилка", StringComparison.OrdinalIgnoreCase)
        ? "error"
        : _status.Contains("готово", StringComparison.OrdinalIgnoreCase)
            ? "ok"
            : "warn";

    protected override async Task OnInitializedAsync()
    {
        _repositories = (await Api.GetRepositoriesAsync()).ToList();
    }

    private async Task GenerateAsync()
    {
        if (_model.RepositoryConnectionId == Guid.Empty)
        {
            _status = "Оберіть репозиторій із бази (меню ☰ → Репозиторії).";
            return;
        }

        var baseTag = _model.BaseTag?.Trim() ?? string.Empty;
        var targetTag = _model.TargetTag?.Trim() ?? string.Empty;
        if (!GitIngestMode.IsFullRepositoryHistory(baseTag, targetTag)
            && (string.IsNullOrWhiteSpace(baseTag) || string.IsNullOrWhiteSpace(targetTag)))
        {
            _status = "Вкажіть Base Tag і Target Tag, або обидва * для повної історії.";
            return;
        }

        IsGenerating = true;
        _status = "Генерація запущена...";
        _document = null;
        _commitsForUi = [];
        _commitsTotal = 0;
        _commitsTruncated = false;

        try
        {
            var result = await Api.GenerateReleaseNotesAsync(_model.RepositoryConnectionId, _model.BaseTag, _model.TargetTag);
            if (!result.Success || result.DocumentId is null)
            {
                _status = $"Помилка генерації release notes. {result.Message}";
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            _document = await Api.GetReleaseNoteDocumentAsync(result.DocumentId.Value);
            RefreshCommitListUi();
            _status = _document is null ? "Результат ще не готовий." : "Готово.";
        }
        catch (Exception ex)
        {
            _status = $"Помилка генерації release notes. {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private void RefreshCommitListUi()
    {
        var entries = _document?.Entries;
        if (entries is null || entries.Count == 0)
        {
            _commitsForUi = [];
            _commitsTotal = 0;
            _commitsTruncated = false;
            return;
        }

        _commitsTotal = entries.Count;
        _commitsTruncated = _commitsTotal > MaxCommitsInUi;
        _commitsForUi = _commitsTruncated
            ? entries.Take(MaxCommitsInUi).ToList()
            : entries.ToList();
    }

    private string? CommitUrl(string commitId)
    {
        if (_document is null || string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        return BuildGitHubCommitUrl(_document.Repository, commitId);
    }

    private static string ShortHash(string id) => id.Length > 7 ? id[..7] : id;

    private static string FormatCommitDate(DateTimeOffset value) =>
        value != default && value.Year >= 1980 ? value.LocalDateTime.ToString("g") : "—";

    private static string? BuildGitHubCommitUrl(string repositoryPath, string commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId))
        {
            return null;
        }

        var path = repositoryPath.Trim().Trim('/');
        if (path.Contains("://", StringComparison.Ordinal) ||
            path.Contains("\\", StringComparison.Ordinal))
        {
            return null;
        }

        if (path.Count(c => c == '/') != 1)
        {
            return null;
        }

        return $"https://github.com/{path}/commit/{commitId.Trim()}";
    }

    private sealed class GenerateModel
    {
        public Guid RepositoryConnectionId { get; set; }
        public string BaseTag { get; set; } = string.Empty;
        public string TargetTag { get; set; } = string.Empty;
    }
}
