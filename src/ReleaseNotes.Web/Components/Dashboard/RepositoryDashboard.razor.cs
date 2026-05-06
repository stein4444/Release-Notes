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
            Status = "Дашборд оновлено.";
        }
        catch (Exception ex)
        {
            Status = $"Помилка дашборду: {ex.Message}";
        }
    }
}
