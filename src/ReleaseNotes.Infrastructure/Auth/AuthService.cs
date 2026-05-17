using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Auth;

public sealed class AuthService(
    ReleaseNotesDbContext db,
    JwtTokenService jwtTokenService) : IAuthService
{
    private readonly PasswordHasher<UserEntity> _passwordHasher = new();

    public async Task<AuthResult> RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return Fail("Вкажіть коректний email.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return Fail("Пароль має бути не менше 8 символів.");
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim();
        if (await db.Users.AsNoTracking().AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return Fail("Користувач з таким email уже існує.");
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        try
        {
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            return Fail($"Не вдалося зберегти користувача: {ex.InnerException?.Message ?? ex.Message}");
        }

        try
        {
            var token = jwtTokenService.CreateToken(user.Id, user.Email, user.DisplayName);
            return new AuthResult(true, token, user.Id, user.Email, user.DisplayName, null);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || string.IsNullOrWhiteSpace(password))
        {
            return Fail("Невірний email або пароль.");
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (user is null)
        {
            return Fail("Невірний email або пароль.");
        }

        var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify is PasswordVerificationResult.Failed)
        {
            return Fail("Невірний email або пароль.");
        }

        var token = jwtTokenService.CreateToken(user.Id, user.Email, user.DisplayName);
        return new AuthResult(true, token, user.Id, user.Email, user.DisplayName, null);
    }

    private static string? NormalizeEmail(string email)
    {
        var trimmed = email.Trim();
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }

    private static AuthResult Fail(string message) =>
        new(false, null, null, null, null, message);
}
