using System.Net.Http.Json;
using System.Text.Json;
using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Web.Services;

public sealed class ReleaseNotesAppApi(HttpClient http) : IReleaseNotesAppApi
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<RepositoryListDto>> GetRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var list = await http.GetFromJsonAsync<List<RepositoryListDto>>("/api/repositories", JsonOptions, cancellationToken);
        return list ?? [];
    }

    public async Task<IReadOnlyList<DashboardRowDto>> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var list = await http.GetFromJsonAsync<List<DashboardRowDto>>("/api/dashboard/repositories", JsonOptions, cancellationToken);
        return list ?? [];
    }

    public async Task<GenerateNotesResult> GenerateReleaseNotesAsync(
        Guid repositoryConnectionId,
        string baseTag,
        string targetTag,
        CancellationToken cancellationToken = default)
    {
        var payload = new { repositoryConnectionId, baseTag, targetTag };
        using var response = await http.PostAsJsonAsync("/api/release-notes/generate", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new GenerateNotesResult(false, null, body);
        }

        var accepted = JsonSerializer.Deserialize<GenerateAcceptedJson>(body, JsonOptions);
        return accepted?.Id is { } id
            ? new GenerateNotesResult(true, id, "Accepted.")
            : new GenerateNotesResult(false, null, "Empty response.");
    }

    public async Task<ReleaseNoteDocument?> GetReleaseNoteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<ReleaseNoteDocument>($"/api/release-notes/{id}", JsonOptions, cancellationToken);
    }

    public async Task SaveRepositoryAsync(RepositoryUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            dto.DisplayName,
            dto.Provider,
            dto.RepositoryPath,
            dto.AccessToken,
            dto.IsActive
        };

        using var response = dto.Id is null
            ? await http.PostAsJsonAsync("/api/repositories", payload, cancellationToken)
            : await http.PutAsJsonAsync($"/api/repositories/{dto.Id}", payload, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteRepositoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"/api/repositories/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SaveIntegrationAsync(IntegrationUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            dto.Provider,
            dto.DisplayName,
            dto.SettingsJson,
            dto.IsEnabled
        };

        await http.PostAsJsonAsync("/api/integrations", payload, cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationListDto>> GetIntegrationsAsync(CancellationToken cancellationToken = default)
    {
        var list = await http.GetFromJsonAsync<List<IntegrationListDto>>("/api/integrations", JsonOptions, cancellationToken);
        return list ?? [];
    }

    private sealed class GenerateAcceptedJson
    {
        public Guid Id { get; set; }
    }
}
