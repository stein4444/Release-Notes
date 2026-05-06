using Microsoft.AspNetCore.Components;
using ReleaseNotes.Web.Services;

namespace ReleaseNotes.Web.Components.Drawer;

public partial class ManagementDrawer
{
    [Inject] public IReleaseNotesAppApi Api { get; set; } = default!;
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnRepositoriesChanged { get; set; }

    private string ActiveTab { get; set; } = "repos";
    private string RepoFormStatus { get; set; } = string.Empty;
    private bool RepoFormIsError { get; set; }
    private List<RepositoryListDto> Repositories { get; set; } = [];
    private List<IntegrationListDto> Integrations { get; set; } = [];
    private RepositoryFormModel RepoForm { get; set; } = new() { Provider = "github", IsActive = true };
    private IntegrationFormModel IntegrationForm { get; set; } = new() { Provider = "gitlab", SettingsJson = "{}", IsEnabled = true };

    protected override async Task OnInitializedAsync()
    {
        await LoadRepositoriesAsync();
        await LoadIntegrationsAsync();
    }

    private Task OnCloseClicked() => OnClose.InvokeAsync();
    private void SetReposTab() => ActiveTab = "repos";
    private void SetServicesTab() => ActiveTab = "services";

    private async Task LoadRepositoriesAsync()
    {
        Repositories = (await Api.GetRepositoriesAsync()).ToList();
    }

    private async Task SaveRepositoryAsync()
    {
        RepoFormStatus = string.Empty;
        if (string.IsNullOrWhiteSpace(RepoForm.DisplayName) ||
            string.IsNullOrWhiteSpace(RepoForm.Provider) ||
            string.IsNullOrWhiteSpace(RepoForm.RepositoryPath))
        {
            RepoFormIsError = true;
            RepoFormStatus = "Заповніть назву, провайдер і шлях репозиторію (owner/repo).";
            return;
        }

        try
        {
            await Api.SaveRepositoryAsync(new RepositoryUpsertDto(
                RepoForm.Id,
                RepoForm.DisplayName,
                RepoForm.Provider,
                RepoForm.RepositoryPath,
                RepoForm.AccessToken,
                RepoForm.IsActive));

            RepoFormIsError = false;
            RepoFormStatus = "Збережено.";
            ClearRepoForm();
            await LoadRepositoriesAsync();
            await OnRepositoriesChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            RepoFormIsError = true;
            RepoFormStatus = $"Помилка збереження: {ex.Message}";
        }
    }

    private void EditRepository(RepositoryListDto repository)
    {
        RepoForm = new RepositoryFormModel
        {
            Id = repository.Id,
            DisplayName = repository.DisplayName,
            Provider = repository.Provider,
            RepositoryPath = repository.RepositoryPath,
            IsActive = repository.IsActive
        };
    }

    private void ClearRepoForm()
    {
        RepoForm = new RepositoryFormModel { Provider = "github", IsActive = true };
    }

    private async Task DeleteRepositoryAsync(Guid id)
    {
        RepoFormStatus = string.Empty;
        try
        {
            await Api.DeleteRepositoryAsync(id);
            RepoFormIsError = false;
            RepoFormStatus = "Видалено.";
            await LoadRepositoriesAsync();
            await OnRepositoriesChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            RepoFormIsError = true;
            RepoFormStatus = $"Помилка видалення: {ex.Message}";
        }
    }

    private async Task LoadIntegrationsAsync()
    {
        Integrations = (await Api.GetIntegrationsAsync()).ToList();
    }

    private async Task SaveIntegrationAsync()
    {
        if (string.IsNullOrWhiteSpace(IntegrationForm.Provider) || string.IsNullOrWhiteSpace(IntegrationForm.DisplayName))
        {
            return;
        }

        await Api.SaveIntegrationAsync(new IntegrationUpsertDto(
            IntegrationForm.Provider,
            IntegrationForm.DisplayName,
            IntegrationForm.SettingsJson,
            IntegrationForm.IsEnabled));

        IntegrationForm = new IntegrationFormModel { Provider = "gitlab", SettingsJson = "{}", IsEnabled = true };
        await LoadIntegrationsAsync();
    }

    private sealed class RepositoryFormModel
    {
        public Guid? Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class IntegrationFormModel
    {
        public string Provider { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SettingsJson { get; set; } = "{}";
        public bool IsEnabled { get; set; }
    }
}
