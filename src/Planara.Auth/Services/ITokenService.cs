using System.Security.Claims;

namespace Planara.Auth.Services;

public interface ITokenService
{
    (string token, DateTime expiresAtUtc) GenerateAccessToken(IEnumerable<Claim> claims);
    (string refreshToken, string refreshTokenHash) GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}