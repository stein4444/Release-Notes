namespace ReleaseNotes.Application.Models;

/// <summary>
/// Режими збору комітів з клону > GitSharp. Для повної історії обидва теги в запиті мають бути маркером (напр. <c>*</c>).
/// </summary>
public static class GitIngestMode
{
    public const string FullHistoryMarker = "*";
    private const string FullHistoryAlt = "__full__";

    public static bool IsFullRepositoryHistory(string baseTag, string targetTag)
    {
        return IsFullMarker(baseTag) && IsFullMarker(targetTag);
    }

    private static bool IsFullMarker(string value)
    {
        var t = value.Trim();
        return t.Length > 0
               && (string.Equals(t, FullHistoryMarker, StringComparison.Ordinal)
                   || string.Equals(t, FullHistoryAlt, StringComparison.OrdinalIgnoreCase));
    }
}
