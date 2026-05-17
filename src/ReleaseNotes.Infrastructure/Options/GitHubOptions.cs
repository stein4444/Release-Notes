namespace ReleaseNotes.Infrastructure.Options;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
    public string Token { get; set; } = string.Empty;

    /// <summary>Максимум комітів у режимі повного збору (<c>git rev-list --all</c>).</summary>
    public int MaxFullHistoryCommits { get; set; } = 50_000;
}
