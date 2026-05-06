namespace ReleaseNotes.Web.Models;

public sealed record ServiceIntegrationRequest(
    string Provider,
    string DisplayName,
    string SettingsJson,
    bool IsEnabled);
