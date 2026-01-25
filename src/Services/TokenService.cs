using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Planara.Auth.Options;

namespace Planara.Auth.Services;

public sealed class TokenService(IOptions<JwtOptions> jwt) : ITokenService
{
    private readonly JwtOptions _jwt = jwt.Value;

    public (string token, DateTime expiresAtUtc) GenerateAccessToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string refreshToken, string refreshTokenHash) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var raw = WebEncoders.Base64UrlEncode(bytes);
        var hash = HashRefreshToken(raw);
        return (raw, hash);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var data = Encoding.UTF8.GetBytes(refreshToken);
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }
}