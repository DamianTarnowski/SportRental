using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SportRental.Infrastructure.Data;

namespace SportRental.Api.Auth;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly byte[] _signingKey;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey configuration is required");
        }

        _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);
    }

    public AuthTokenResult CreateToken(ApplicationUser user, Guid tenantId, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("tenant-id", tenantId.ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var credentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        return new AuthTokenResult(handler.WriteToken(token), expiresAtUtc);
    }

    public int GetAccessTokenLifetimeSeconds() => _options.AccessTokenLifetimeMinutes * 60;
    
    public int GetRefreshTokenLifetimeDays() => _options.RefreshTokenLifetimeDays;

    public sealed record AuthTokenResult(string AccessToken, DateTime ExpiresAtUtc);
}
