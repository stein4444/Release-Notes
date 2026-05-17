using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ReleaseNotes.Infrastructure.Options;

namespace ReleaseNotes.Infrastructure.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    public string CreateToken(Guid userId, string email, string displayName)
    {
        var jwt = options.Value;
        if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Secret має бути не менше 32 символів (appsettings або User Secrets).");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(jwt.ExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
