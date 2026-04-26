using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Options;

namespace ReleaseNotes.Infrastructure.Clients;

public sealed class GitHubApiClient(
    HttpClient httpClient,
    IOptions<GitHubOptions> options,
    IMemoryCache memoryCache) : IGitSourceClient
{
    public async Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(string repository, string baseTag, string targetTag, CancellationToken cancellationToken)
    {
        var cacheKey = $"gh:{repository}:{baseTag}:{targetTag}";
        if (memoryCache.TryGetValue(cacheKey, out IReadOnlyCollection<SourceArtifact>? cached) && cached is not null)
        {
            return cached;
        }

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ReleaseNotesAutomation/1.0");
        if (!string.IsNullOrWhiteSpace(options.Value.Token))
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Value.Token);
        }

        var compareEndpoint = $"{options.Value.ApiBaseUrl.TrimEnd('/')}/repos/{repository}/compare/{baseTag}...{targetTag}";
        var compareResult = await httpClient.GetFromJsonAsync<GitHubCompareResponse>(compareEndpoint, cancellationToken);
        var artifacts = compareResult?.Commits?.Select(c => new SourceArtifact(
            c.Sha,
            c.Commit.Message.Split('\n')[0],
            c.Commit.Message,
            c.Author?.Login ?? "unknown",
            Array.Empty<string>(),
            Array.Empty<string>(),
            c.Commit.Author.Date)).ToArray() ?? [];

        memoryCache.Set(cacheKey, artifacts, TimeSpan.FromMinutes(10));
        return artifacts;
    }

    private sealed class GitHubCompareResponse
    {
        public IReadOnlyCollection<GitHubCommit>? Commits { get; set; }
    }

    private sealed class GitHubCommit
    {
        public string Sha { get; set; } = string.Empty;
        public GitHubCommitDetail Commit { get; set; } = new();
        public GitHubAuthor? Author { get; set; }
    }

    private sealed class GitHubCommitDetail
    {
        public string Message { get; set; } = string.Empty;
        public GitHubCommitAuthor Author { get; set; } = new();
    }

    private sealed class GitHubCommitAuthor
    {
        public DateTimeOffset Date { get; set; }
    }

    private sealed class GitHubAuthor
    {
        public string Login { get; set; } = string.Empty;
    }
}
