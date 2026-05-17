using Microsoft.AspNetCore.Components;
using ReleaseNotes.Web.Services;

namespace ReleaseNotes.Web.Components.Dashboard;

public partial class RepositoryDashboard
{
    [Inject] public IReleaseNotesAppApi Api { get; set; } = default!;

    private List<DashboardRowDto> Items { get; set; } = [];
    private string Status { get; set; } = string.Empty;
    private string StatusClass => Status.Contains("помилка", StringComparison.OrdinalIgnoreCase) ? "error" : "ok";

    protected override async Task OnInitializedAsync()
    {
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            Items = (await Api.GetDashboardAsync()).ToList();
            var totalCommits = Items.Sum(i => i.CommitList.Count);
            Status = totalCommits > 0
                ? $"Список комітів оновлено ({totalCommits} у {Items.Count} репо)."
                : "Немає комітів у документах — згенеруйте release notes (* / *).";
        }
        catch (Exception ex)
        {
            Status = $"Помилка дашборду: {ex.Message}";
        }
    }

    private static string ShortHash(string id) => id.Length > 7 ? id[..7] : id;

    private static string? FormatIso(DateTimeOffset? dt) =>
        dt is { Year: >= 1980 } d ? d.ToString("O") : null;

    private static string FormatCommitDate(DateTimeOffset? dt) =>
        dt is { Year: >= 1980 } d ? d.LocalDateTime.ToString("g") : "—";

    private static string? CommitUrl(string repositoryPath, string commitId)
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
}
