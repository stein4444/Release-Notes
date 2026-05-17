using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReleaseNotes.Infrastructure.Persistence;

public static class ReleaseNoteJson
{
    public static readonly JsonSerializerOptions EntryOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool IsValidCommitDate(DateTimeOffset value) =>
        value != default && value.Year >= 1980;
}
