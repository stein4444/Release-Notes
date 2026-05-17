namespace ReleaseNotes.Infrastructure.Utilities;

public static class RepositoryPathNormalizer
{
    /// <summary>
    /// Strip github URL prefixes so stored path is always canonical <c>owner/repo</c>.
    /// </summary>
    public static string Normalize(string repositoryPath)
    {
        var p = repositoryPath.Trim().Trim('/');
        const string httpsPrefix = "https://github.com/";
        const string httpPrefix = "http://github.com/";
        if (p.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            p = p[httpsPrefix.Length..];
        }
        else if (p.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            p = p[httpPrefix.Length..];
        }

        if (p.StartsWith("repos/", StringComparison.OrdinalIgnoreCase))
        {
            p = p["repos/".Length..];
        }

        if (p.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            p = p[..^4];
        }

        return p.Trim('/');
    }

    public static (string Owner, string Repo) ParseOwnerRepo(string normalizedPath)
    {
        var slash = normalizedPath.IndexOf('/');
        var second = slash >= 0 ? normalizedPath.IndexOf('/', slash + 1) : -1;
        if (slash <= 0 || slash == normalizedPath.Length - 1 || second >= 0)
        {
            throw new InvalidOperationException(
                $"Repository Path має бути рівно 'owner/repo' або повний URL https://github.com/owner/repo. Зараз: '{normalizedPath}'.");
        }

        return (normalizedPath[..slash], normalizedPath[(slash + 1)..]);
    }
}
