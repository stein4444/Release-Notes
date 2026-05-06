using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Options;

namespace ReleaseNotes.Infrastructure.Clients;

public sealed class GitHubApiClient(
    HttpClient httpClient,
    IOptions<GitHubOptions> options,
    IMemoryCache memoryCache) : IGitSourceClient
{
    public async Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(GitHubCompareRequest request, CancellationToken cancellationToken)
    {
        var tokenFingerprint = GetTokenFingerprint(request.AccessToken, options.Value.Token);
        var cacheKey = $"gh:{request.CredentialScopeId:N}:{request.RepositoryPath}:{request.BaseTag}:{request.TargetTag}:{tokenFingerprint}";
        if (memoryCache.TryGetValue(cacheKey, out IReadOnlyCollection<SourceArtifact>? cached) && cached is not null)
        {
            return cached;
        }

        var bearer = string.IsNullOrWhiteSpace(request.AccessToken)
            ? options.Value.Token
            : request.AccessToken;

        var compareEndpoint = $"{options.Value.ApiBaseUrl.TrimEnd('/')}/repos/{request.RepositoryPath}/compare/{request.BaseTag}...{request.TargetTag}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, compareEndpoint);
        httpRequest.Headers.UserAgent.ParseAdd("ReleaseNotesAutomation/1.0");
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var compareResult = await response.Content.ReadFromJsonAsync<GitHubCompareResponse>(cancellationToken: cancellationToken);
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
