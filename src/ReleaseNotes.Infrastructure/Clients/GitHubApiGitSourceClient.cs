using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Options;
using ReleaseNotes.Infrastructure.Utilities;

namespace ReleaseNotes.Infrastructure.Clients;

/// <summary>
/// Завантажує лише метадані комітів через GitHub REST API — без git clone.
/// </summary>
public sealed class GitHubApiGitSourceClient(
    IOptions<GitHubOptions> options,
    IMemoryCache memoryCache,
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubApiGitSourceClient> logger) : IGitSourceClient
{
    private const int CompareCommitLimit = 250;
    private const int PageSize = 100;

    public async Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(
        GitHubCompareRequest request,
        CancellationToken cancellationToken)
    {
        var tokenFingerprint = GetTokenFingerprint(request.AccessToken, options.Value.Token);
        var normalizedPath = RepositoryPathNormalizer.Normalize(request.RepositoryPath);
        var cacheKey = $"github-api:v1:{request.CredentialScopeId:N}:{normalizedPath}:{request.BaseTag}:{request.TargetTag}:{tokenFingerprint}";
        if (memoryCache.TryGetValue(cacheKey, out IReadOnlyCollection<SourceArtifact>? cached) && cached is not null)
        {
            return cached;
        }

        var bearer = ResolveToken(request.AccessToken);
        var (owner, repo) = RepositoryPathNormalizer.ParseOwnerRepo(normalizedPath);
        var baseRef = request.BaseTag.Trim();
        var headRef = request.TargetTag.Trim();

        IReadOnlyList<SourceArtifact> artifacts;
        if (GitIngestMode.IsFullRepositoryHistory(baseRef, headRef))
        {
            var max = Math.Clamp(options.Value.MaxFullHistoryCommits, 1, 500_000);
            artifacts = await FetchAllCommitsAsync(bearer, owner, repo, max, cancellationToken);
            logger.LogInformation(
                "GitHub API: повний збір {Count} комітів (max {Max}) для {Repo}",
                artifacts.Count,
                max,
                normalizedPath);
        }
        else
        {
            artifacts = await FetchRangeCommitsAsync(bearer, owner, repo, baseRef, headRef, cancellationToken);
            logger.LogInformation(
                "GitHub API: {Count} commits between {Base} and {Head} for {Repo}",
                artifacts.Count,
                baseRef,
                headRef,
                normalizedPath);
        }

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException("У вибраному діапазоні немає комітів.");
        }

        memoryCache.Set(cacheKey, artifacts, TimeSpan.FromMinutes(10));
        return artifacts;
    }

    private async Task<IReadOnlyList<SourceArtifact>> FetchRangeCommitsAsync(
        string? bearer,
        string owner,
        string repo,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken)
    {
        var comparePath =
            $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/compare/{Uri.EscapeDataString(baseRef)}...{Uri.EscapeDataString(headRef)}";
        var compare = await GetAsync<GitHubCompareResponse>(bearer, comparePath, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Не вдалося порівняти {baseRef}...{headRef}. Перевірте теги/SHA та доступ токена.");

        var commits = compare.Commits ?? [];
        if (compare.TotalCommits > commits.Count && commits.Count >= CompareCommitLimit)
        {
            var headSha = compare.Commits?.LastOrDefault()?.Sha ?? headRef;
            var since = compare.BaseCommit?.Commit?.Committer?.Date
                        ?? compare.BaseCommit?.Commit?.Author?.Date;
            var extra = await FetchCommitsPagedAsync(
                bearer,
                owner,
                repo,
                headSha,
                since,
                until: null,
                maxCount: compare.TotalCommits,
                cancellationToken);
            commits = MergeCommits(commits, extra);
        }

        return MapCommits(commits);
    }

    private async Task<IReadOnlyList<SourceArtifact>> FetchAllCommitsAsync(
        string? bearer,
        string owner,
        string repo,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var raw = await FetchCommitsPagedAsync(
            bearer,
            owner,
            repo,
            sha: null,
            since: null,
            until: null,
            maxCount,
            cancellationToken);

        raw.Sort((a, b) => ResolveDate(a).CompareTo(ResolveDate(b)));
        return MapCommits(raw);
    }

    private async Task<List<GitHubCommitItem>> FetchCommitsPagedAsync(
        string? bearer,
        string owner,
        string repo,
        string? sha,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var result = new List<GitHubCommitItem>(Math.Min(maxCount, PageSize));
        var page = 1;

        while (result.Count < maxCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = new List<string> { $"per_page={PageSize}", $"page={page}" };
            if (!string.IsNullOrWhiteSpace(sha))
            {
                query.Add($"sha={Uri.EscapeDataString(sha)}");
            }

            if (since is { } s)
            {
                query.Add($"since={Uri.EscapeDataString(s.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");
            }

            if (until is { } u)
            {
                query.Add($"until={Uri.EscapeDataString(u.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))}");
            }

            var path =
                $"/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits?{string.Join('&', query)}";
            var pageItems = await GetAsync<List<GitHubCommitItem>>(bearer, path, cancellationToken);
            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                if (result.Count >= maxCount)
                {
                    break;
                }

                result.Add(item);
            }

            if (pageItems.Count < PageSize)
            {
                break;
            }

            page++;
        }

        return result;
    }

    private static List<GitHubCommitItem> MergeCommits(
        IReadOnlyList<GitHubCommitItem> primary,
        IReadOnlyList<GitHubCommitItem> extra)
    {
        var map = new Dictionary<string, GitHubCommitItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in primary)
        {
            if (!string.IsNullOrWhiteSpace(c.Sha))
            {
                map[c.Sha] = c;
            }
        }

        foreach (var c in extra)
        {
            if (!string.IsNullOrWhiteSpace(c.Sha))
            {
                map[c.Sha] = c;
            }
        }

        return map.Values
            .OrderBy(c => ResolveDate(c))
            .ToList();
    }

    private static IReadOnlyList<SourceArtifact> MapCommits(IReadOnlyList<GitHubCommitItem> commits) =>
        commits
            .Where(c => !string.IsNullOrWhiteSpace(c.Sha))
            .Select(c =>
            {
                var message = c.Commit?.Message ?? string.Empty;
                var title = message.Split('\n')[0].Trim();
                var author = c.Commit?.Author?.Name ?? "unknown";
                var committedAt = ResolveDate(c);
                return new SourceArtifact(
                    c.Sha,
                    string.IsNullOrEmpty(title) ? c.Sha[..Math.Min(7, c.Sha.Length)] : title,
                    message,
                    author,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    committedAt);
            })
            .ToList();

    private static DateTimeOffset ResolveDate(GitHubCommitItem item) =>
        item.Commit?.Committer?.Date
        ?? item.Commit?.Author?.Date
        ?? DateTimeOffset.UtcNow;

    private HttpClient CreateHttpClient()
    {
        var http = httpClientFactory.CreateClient(nameof(GitHubApiGitSourceClient));
        if (http.BaseAddress is null)
        {
            var baseUrl = options.Value.ApiBaseUrl.TrimEnd('/');
            http.BaseAddress = new Uri(baseUrl + "/");
        }

        return http;
    }

    private string? ResolveToken(string? connectionToken)
    {
        var effective = string.IsNullOrWhiteSpace(connectionToken)
            ? options.Value.Token
            : connectionToken;
        return string.IsNullOrWhiteSpace(effective) ? null : effective.Trim();
    }

    private async Task<T?> GetAsync<T>(string? bearer, string path, CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ReleaseNotesAutomation", "1.0"));
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var snippet = body.Length > 300 ? body[..300] : body;
        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => new InvalidOperationException(
                $"GitHub API: ресурс не знайдено ({path}). Перевірте owner/repo, теги або SHA."),
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                new InvalidOperationException(
                    "GitHub API: відмовлено в доступі. Додайте PAT з правами repo у підключенні або GitHub:Token."),
            _ => new InvalidOperationException(
                $"GitHub API помилка {(int)response.StatusCode}: {snippet}"),
        };
    }

    private static string GetTokenFingerprint(string? connectionToken, string globalToken)
    {
        var effective = string.IsNullOrWhiteSpace(connectionToken) ? globalToken : connectionToken;
        if (string.IsNullOrWhiteSpace(effective))
        {
            return "none";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(effective));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private sealed class GitHubCompareResponse
    {
        [JsonPropertyName("total_commits")]
        public int TotalCommits { get; init; }

        [JsonPropertyName("commits")]
        public List<GitHubCommitItem>? Commits { get; init; }

        [JsonPropertyName("base_commit")]
        public GitHubCommitItem? BaseCommit { get; init; }
    }

    private sealed class GitHubCommitItem
    {
        [JsonPropertyName("sha")]
        public string Sha { get; init; } = string.Empty;

        [JsonPropertyName("commit")]
        public GitHubCommitDetail? Commit { get; init; }
    }

    private sealed class GitHubCommitDetail
    {
        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("author")]
        public GitHubPerson? Author { get; init; }

        [JsonPropertyName("committer")]
        public GitHubPerson? Committer { get; init; }
    }

    private sealed class GitHubPerson
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("date")]
        public DateTimeOffset? Date { get; init; }
    }
}
