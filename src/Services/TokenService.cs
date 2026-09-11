using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Planara.Auth.Registration;
using Planara.Common.Auth.Claims;
using Planara.Common.Auth.Jwt;

using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.Services;

public class TokenService(IOptions<JwtOptions> jwt) : ITokenService
{
    private readonly JwtOptions _jwt = jwt.Value;

    public (string token, DateTime expiresAtUtc) GenerateAccessToken(IEnumerable<Claim> claims)
    {
        var now = DateTime.UtcNow;

        var expiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);
        
        var tokenClaims = claims.Where(x => x.Type != ClaimTypes.TokenUse)
            .Append(new Claim(ClaimTypes.TokenUse, TokenUses.Access));

        var token = GenerateToken(tokenClaims, now, expiresAt);

        return (token, expiresAt);
    }

    public (string refreshToken, string refreshTokenHash) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var raw = WebEncoders.Base64UrlEncode(bytes);
        var hash = HashRefreshToken(raw);
        return (raw, hash);
    }
    
    public string GenerateRegistrationToken(Guid registrationId, RegistrationAuthLevel authLevel, DateTime expiresAtUtc)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.UserId, registrationId.ToString()),
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
            new Claim(RegistrationClaimTypes.AuthLevel, authLevel.ToString())
        };

        return GenerateToken(claims, DateTime.UtcNow, expiresAtUtc);
    }


    public string HashRefreshToken(string refreshToken)
    {
        var data = Encoding.UTF8.GetBytes(refreshToken);
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }
    
    public ClaimsPrincipal? ValidateRegistrationToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        try
        {
            var principal = handler.ValidateToken(token, CreateValidationParameters(), out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            {
                return null;
            }

            var tokenUse = principal.FindFirst(ClaimTypes.TokenUse)?.Value;

            if (tokenUse != TokenUses.Registration)
                return null;
            
            _ = principal.GetUserId();
            _ = principal.GetRegistrationAuthLevel();

            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
    
    private string GenerateToken(IEnumerable<Claim> claims, DateTime notBeforeUtc, DateTime expiresAtUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
                issuer: _jwt.Issuer,
                audience: _jwt.Audience,
                claims: claims,
                notBefore: notBeforeUtc,
                expires: expiresAtUtc,
                signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    private TokenValidationParameters CreateValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = _jwt.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }
}