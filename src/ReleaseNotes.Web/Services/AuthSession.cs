using ReleaseNotes.Web.Models;

namespace ReleaseNotes.Web.Services;

public sealed class AuthSession
{
    public string? Token { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && UserId is not null;

    public void Set(AuthResponse response)
    {
        Token = response.Token;
        UserId = response.UserId;
        Email = response.Email;
        DisplayName = response.DisplayName;
    }

    public void Clear()
    {
        Token = null;
        UserId = null;
        Email = null;
        DisplayName = null;
    }
}
