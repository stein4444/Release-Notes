using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GitSharp;
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
/// Клон через системний <c>git</c> (GitSharp <see cref="GitSharp.Core.Transport.URIish"/> на Windows викликає
/// <c>Path.GetFullPath</c> для рядків <c>https://...</c> і ламає URL). Обхід комітів — GitSharp по локальному репо.
/// </summary>
public sealed class GitSharpGitSourceClient(
    IOptions<GitHubOptions> options,
    IMemoryCache memoryCache,
    ILogger<GitSharpGitSourceClient> logger) : IGitSourceClient
{
    public async Task<IReadOnlyCollection<SourceArtifact>> GetArtifactsAsync(GitHubCompareRequest request, CancellationToken cancellationToken)
    {
        var tokenFingerprint = GetTokenFingerprint(request.AccessToken, options.Value.Token);
        var normalizedPath = RepositoryPathNormalizer.Normalize(request.RepositoryPath);
        var cacheKey = $"gitsharp:v2:{request.CredentialScopeId:N}:{normalizedPath}:{request.BaseTag}:{request.TargetTag}:{tokenFingerprint}";
        if (memoryCache.TryGetValue(cacheKey, out IReadOnlyCollection<SourceArtifact>? cached) && cached is not null)
        {
            return cached;
        }

        var artifacts = await Task.Run(
            () => CloneAndCollectCommits(request, normalizedPath, cancellationToken),
            cancellationToken);

        memoryCache.Set(cacheKey, artifacts, TimeSpan.FromMinutes(10));
        return artifacts;
    }

    private IReadOnlyCollection<SourceArtifact> CloneAndCollectCommits(
        GitHubCompareRequest request,
        string normalizedPath,
        CancellationToken cancellationToken)
    {
        var (owner, repo) = RepositoryPathNormalizer.ParseOwnerRepo(normalizedPath);
        var bearer = string.IsNullOrWhiteSpace(request.AccessToken)
            ? options.Value.Token
            : request.AccessToken;
        bearer = string.IsNullOrWhiteSpace(bearer) ? null : bearer.Trim();

        var cloneUrl = BuildHttpsCloneUrl(owner, repo, bearer);
        var parentDir = Path.Combine(Path.GetTempPath(), "release-notes-gitsharp", request.CredentialScopeId.ToString("N"));
        Directory.CreateDirectory(parentDir);
        var workRoot = Path.Combine(parentDir, Guid.NewGuid().ToString("N"));
        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }

        Repository? repository = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            RunGitClone(cloneUrl, workRoot, cancellationToken);
            TryFetchTags(workRoot, cancellationToken);

            repository = new Repository(workRoot);
            var commitDatesBySha = LoadCommitDatesFromGit(workRoot, cancellationToken);
            var baseRef = request.BaseTag.Trim();
            var headRef = request.TargetTag.Trim();

            if (GitIngestMode.IsFullRepositoryHistory(baseRef, headRef))
            {
                var max = Math.Clamp(options.Value.MaxFullHistoryCommits, 1, 500_000);
                var stdout = RunGitInRepoStdout(
                    workRoot,
                    new[]
                    {
                        "rev-list",
                        "--all",
                        "--reverse",
                        "--max-count",
                        max.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    cancellationToken);

                var shas = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var commits = new List<Commit>(shas.Length);
                foreach (var sha in shas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var c = TryGetCommit(repository, sha);
                    if (c is not null)
                    {
                        commits.Add(c);
                    }
                }

                commits.Sort((a, b) => ResolveCommitDate(a, commitDatesBySha).CompareTo(ResolveCommitDate(b, commitDatesBySha)));
                if (commits.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Повний збір: у клоні немає комітів (git rev-list --all порожній). Перевір доступ і репозиторій.");
                }

                var fullResult = MapCommitsToArtifacts(commits, commitDatesBySha);
                logger.LogInformation(
                    "GitSharp: повний збір {Count} комітів (max-count {Max}) для {Repo}",
                    fullResult.Count,
                    max,
                    normalizedPath);

                return fullResult;
            }

            var tagsHint = FormatLocalTagsHint(workRoot, cancellationToken);

            var baseCommit = TryResolveCommitViaGitThenSharp(workRoot, repository, baseRef, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Не знайдено commit/ref для BaseTag '{baseRef}'. Перевірте ім'я тега або SHA. Локальні теги після clone/fetch: {tagsHint}");

            var headCommit = TryResolveCommitViaGitThenSharp(workRoot, repository, headRef, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Не знайдено commit/ref для TargetTag '{headRef}'. Перевірте ім'я тега або SHA. Локальні теги після clone/fetch: {tagsHint}");

            var rangeCommits = new List<Commit>();
            CollectCommitsBetween(baseCommit, headCommit, rangeCommits);

            rangeCommits.Sort((a, b) => ResolveCommitDate(a, commitDatesBySha).CompareTo(ResolveCommitDate(b, commitDatesBySha)));

            var result = MapCommitsToArtifacts(rangeCommits, commitDatesBySha);

            logger.LogInformation(
                "GitSharp: {Count} commits between {Base} and {Head} for {Repo}",
                result.Count,
                baseRef,
                headRef,
                normalizedPath);

            return result;
        }
        finally
        {
            try
            {
                repository?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "GitSharp: dispose repository");
            }

            try
            {
                if (Directory.Exists(workRoot))
                {
                    Directory.Delete(workRoot, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GitSharp: failed to delete temp dir {Path}", workRoot);
            }
        }
    }

    private IReadOnlyDictionary<string, DateTimeOffset> LoadCommitDatesFromGit(
        string workRoot,
        CancellationToken cancellationToken)
    {
        try
        {
            var stdout = RunGitInRepoStdout(
                workRoot,
                new[] { "log", "--all", "--format=%H%x1f%cI" },
                cancellationToken);

            var dict = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sep = line.IndexOf('\x1f');
                if (sep <= 0 || sep >= line.Length - 1)
                {
                    continue;
                }

                var sha = line[..sep];
                var dateText = line[(sep + 1)..];
                if (DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                {
                    dict[sha] = dto;
                }
            }

            return dict;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "git log --format для дат комітів не виконано в {Path}", workRoot);
            return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DateTimeOffset ResolveCommitDate(
        Commit commit,
        IReadOnlyDictionary<string, DateTimeOffset> datesBySha)
    {
        if (commit.Hash is { } hash
            && datesBySha.TryGetValue(hash, out var fromGit))
        {
            return fromGit;
        }

        return commit.AuthorDate;
    }

    private static IReadOnlyList<SourceArtifact> MapCommitsToArtifacts(
        IReadOnlyList<Commit> commits,
        IReadOnlyDictionary<string, DateTimeOffset> datesBySha) =>
        commits
            .Select(c =>
            {
                var message = c.Message ?? string.Empty;
                var title = message.Split('\n')[0].Trim();
                var author = c.Author?.Name ?? "unknown";
                var hash = c.Hash ?? string.Empty;
                var committedAt = ResolveCommitDate(c, datesBySha);
                return new SourceArtifact(
                    hash,
                    string.IsNullOrEmpty(title) ? c.ShortHash ?? c.Hash ?? "commit" : title,
                    message,
                    author,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    committedAt);
            })
            .ToList();

    private static string RunGitInRepoStdout(string workRoot, string[] gitArgs, CancellationToken cancellationToken)
    {
        var gitExe = ResolveGitExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in gitArgs)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', gitArgs)}: код {process.ExitCode}. {err.Trim()}");
        }

        return stdout;
    }

    private static void CollectCommitsBetween(Commit baseCommit, Commit headCommit, List<Commit> outCommits)
    {
        ArgumentNullException.ThrowIfNull(baseCommit);
        ArgumentNullException.ThrowIfNull(headCommit);

        var ancestorsOfBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (baseCommit.Hash is { } bh)
        {
            ancestorsOfBase.Add(bh);
        }

        foreach (var a in baseCommit.Ancestors)
        {
            if (a?.Hash is { } h)
            {
                ancestorsOfBase.Add(h);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<Commit>();
        stack.Push(headCommit);

        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (!c.IsValid || c.Hash is null)
            {
                continue;
            }

            if (!visited.Add(c.Hash))
            {
                continue;
            }

            if (ancestorsOfBase.Contains(c.Hash))
            {
                continue;
            }

            outCommits.Add(c);

            foreach (var p in c.Parents)
            {
                if (p is { IsValid: true })
                {
                    stack.Push(p);
                }
            }
        }
    }

    /// <summary>
    /// Спочатку SHA через системний <c>git rev-parse</c> (реальні refs у <c>.git</c>), потім GitSharp по ref —
    /// після <c>clone --no-checkout</c> GitSharp часто не резолвить теги.
    /// </summary>
    private static Commit? TryResolveCommitViaGitThenSharp(
        string workRoot,
        Repository repository,
        string refName,
        CancellationToken cancellationToken)
    {
        var sha = TryResolveShaViaGit(workRoot, refName, cancellationToken);
        var bySha = TryGetCommit(repository, sha);
        if (bySha is not null)
        {
            return bySha;
        }

        return TryResolveCommit(repository, refName);
    }

    private static Commit? TryGetCommit(Repository repository, string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        var id = objectId.Trim();
        try
        {
            var c = repository.Get<Commit>(id);
            return c is { IsValid: true } ? c : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveShaViaGit(string workRoot, string refName, CancellationToken cancellationToken)
    {
        var trimmed = refName.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (LooksLikeObjectId(trimmed)
            && TryGitRevParseVerify(workRoot, trimmed, cancellationToken) is { } direct)
        {
            return direct;
        }

        var candidates = new List<string>();
        if (trimmed.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"{trimmed}^{{commit}}");
            candidates.Add(trimmed);
        }
        else if (trimmed.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(trimmed);
        }
        else
        {
            candidates.Add($"refs/tags/{trimmed}^{{commit}}");
            candidates.Add($"{trimmed}^{{commit}}");
            candidates.Add($"refs/tags/{trimmed}");
            candidates.Add($"refs/heads/{trimmed}");
            candidates.Add($"refs/remotes/origin/{trimmed}");
            candidates.Add(trimmed);
        }

        foreach (var c in candidates.Distinct(StringComparer.Ordinal))
        {
            if (TryGitRevParseVerify(workRoot, c, cancellationToken) is { } sha)
            {
                return sha;
            }
        }

        return null;
    }

    private static string? TryGitRevParseVerify(string workRoot, string revision, CancellationToken cancellationToken)
    {
        var gitExe = ResolveGitExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--verify");
        psi.ArgumentList.Add(revision);

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var line = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(line) || !LooksLikeObjectId(line))
        {
            return null;
        }

        return line;
    }

    private static bool LooksLikeObjectId(string value)
    {
        if (value.Length is < 7 or > 40)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatLocalTagsHint(string workRoot, CancellationToken cancellationToken)
    {
        try
        {
            var (code, stdout, _) = RunGitInRepoNoThrow(workRoot, new[] { "tag", "-l" }, cancellationToken);
            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return "(немає даних)";
            }

            var tags = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(40)
                .ToArray();

            return tags.Length == 0 ? "(порожньо)" : string.Join(", ", tags);
        }
        catch
        {
            return "(не вдалося прочитати)";
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGitInRepoNoThrow(
        string workRoot,
        string[] gitArgs,
        CancellationToken cancellationToken)
    {
        var gitExe = ResolveGitExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in gitArgs)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        return (process.ExitCode, stdout, stderr);
    }

    private static Commit? TryResolveCommit(Repository repository, string refName)
    {
        if (string.IsNullOrWhiteSpace(refName))
        {
            return null;
        }

        var trimmed = refName.Trim();
        var candidates = new List<string> { trimmed };
        if (!trimmed.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"refs/tags/{trimmed}");
            candidates.Add($"refs/heads/{trimmed}");
            candidates.Add($"refs/remotes/origin/{trimmed}");
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Commit? c;
            try
            {
                c = repository.Get<Commit>(candidate);
            }
            catch
            {
                c = null;
            }

            if (c is not null && c.IsValid)
            {
                return c;
            }
        }

        return null;
    }

    private void TryFetchTags(string workRoot, CancellationToken cancellationToken)
    {
        try
        {
            RunGitInRepo(workRoot, new[] { "fetch", "origin", "--prune", "--tags", "--quiet" }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "git fetch origin --tags у репозиторії {Path} не виконано або не вдалося", workRoot);
        }
    }

    private static void RunGitInRepo(string workRoot, string[] gitArgs, CancellationToken cancellationToken)
    {
        var gitExe = ResolveGitExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = gitExe,
            WorkingDirectory = workRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in gitArgs)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            _ = process.StandardOutput.ReadToEnd();
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', gitArgs)}: код {process.ExitCode}. {err.Trim()}");
        }
    }

    private string BuildHttpsCloneUrl(string owner, string repo, string? token)
    {
        var host = DeriveCloneHost(options.Value.ApiBaseUrl);
        // Must be a real absolute Uri — a raw "https://..." string can be mistaken for a path (https: as drive) on Windows.
        var path = $"/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}.git";
        var ub = new UriBuilder(Uri.UriSchemeHttps, host, -1, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            ub.UserName = "x-access-token";
            ub.Password = token.Trim();
        }

        var uri = ub.Uri;
        if (!uri.IsAbsoluteUri)
        {
            throw new InvalidOperationException("Не вдалося сформувати абсолютний HTTPS URL для git clone.");
        }

        // AbsoluteUri інколи прибирає UserInfo з міркувань безпеки — для PAT потрібен повний URL.
        var url = uri.AbsoluteUri;
        if (!string.IsNullOrWhiteSpace(token) && string.IsNullOrEmpty(uri.UserInfo))
        {
            url = ub.ToString();
        }

        return url;
    }

    /// <summary>
    /// GitSharp <c>CloneCommand</c> на Windows ламає HTTPS URL; системний git коректно клонує з PAT.
    /// </summary>
    private static void RunGitClone(string cloneUrl, string workRoot, CancellationToken cancellationToken)
    {
        var gitExe = ResolveGitExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = gitExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("clone");
        psi.ArgumentList.Add("--no-checkout");
        psi.ArgumentList.Add("--quiet");
        psi.ArgumentList.Add(cloneUrl);
        psi.ArgumentList.Add(workRoot);

        using var process = new Process { StartInfo = psi };
        process.Start();

        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            _ = process.StandardOutput.ReadToEnd();
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"git clone завершився з кодом {process.ExitCode}. Переконайся, що встановлено Git і PAT має доступ до репозиторію. Деталі: {err.Trim()}");
        }
    }

    private static string ResolveGitExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("GIT_EXE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        return OperatingSystem.IsWindows() ? "git.exe" : "git";
    }

    private static string DeriveCloneHost(string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return "github.com";
        }

        var uri = new Uri(apiBaseUrl.TrimEnd('/') + "/");
        var host = uri.Host;
        if (host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
        {
            return host["api.".Length..];
        }

        return host;
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
}
