namespace ReleaseNotes.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken);
}

public sealed record AuthResult(
    bool Success,
    string? Token,
    Guid? UserId,
    string? Email,
    string? DisplayName,
    string? ErrorMessage);
