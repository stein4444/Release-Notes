using ReleaseNotes.Web.Models;

namespace ReleaseNotes.Web.Services;

public sealed record AuthClientResult(bool Ok, AuthResponse? Data, string Error)
{
    public static AuthClientResult Success(AuthResponse data) => new(true, data, string.Empty);

    public static AuthClientResult Failure(string error) => new(false, null, error);
}
