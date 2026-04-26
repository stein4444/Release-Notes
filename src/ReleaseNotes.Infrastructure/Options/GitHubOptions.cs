namespace ReleaseNotes.Infrastructure.Options;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
    public string Token { get; set; } = string.Empty;
}
