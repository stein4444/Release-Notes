namespace ReleaseNotes.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "ReleaseNotes";

    public string Audience { get; set; } = "ReleaseNotes.Web";

    public int ExpirationMinutes { get; set; } = 60 * 24;
}
