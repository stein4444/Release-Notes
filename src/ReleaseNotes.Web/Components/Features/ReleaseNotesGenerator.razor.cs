using Microsoft.AspNetCore.Components;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Web.Services;

namespace ReleaseNotes.Web.Components.Features;

public partial class ReleaseNotesGenerator
{
    [Inject] public IReleaseNotesAppApi Api { get; set; } = default!;

    private GenerateModel _model = new()
    {
        BaseTag = "v1.0.0",
        TargetTag = "v1.1.0"
    };

    private List<RepositoryListDto> _repositories = [];

    private IEnumerable<RepositoryListDto> ActiveGithubRepositories =>
        _repositories.Where(r => r.IsActive && string.Equals(r.Provider, "github", StringComparison.OrdinalIgnoreCase));

    private string _status = string.Empty;
    private bool IsGenerating { get; set; }
    private ReleaseNoteDocument? _document;

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

        IsGenerating = true;
        _status = "Генерація запущена...";
        _document = null;

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

    private sealed class GenerateModel
    {
        public Guid RepositoryConnectionId { get; set; }
        public string BaseTag { get; set; } = string.Empty;
        public string TargetTag { get; set; } = string.Empty;
    }
}
