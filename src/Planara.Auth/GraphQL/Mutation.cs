using System.Security.Claims;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Auth.Services;
using Planara.Common.Exceptions;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.GraphQL;

public sealed record RegisterInput(string Email, string Password);
public sealed record LoginInput(string Email, string Password);
public sealed record RefreshInput(string RefreshToken);
public sealed record LogoutInput(string RefreshToken);

public sealed record AuthPayload(
    string AccessToken,
    DateTime AccessExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc
);

public sealed record LogoutPayload(bool Ok);

[ExtendObjectType(OperationTypeNames.Mutation)]
public class Mutation
{
    public async Task<AuthPayload> Register(
        RegisterInput input,
        [Service] DataContext db,
        [Service] ITokenService tokens,
        CancellationToken ct)
    {
        var email = input.Email.Trim().ToLowerInvariant();

        var exists = await db.UserCredentials.AnyAsync(x => x.Email == email, ct);
        if (exists)
            throw new GraphQLException("Email already registered");

        var userId = Guid.NewGuid();
        var hash = BCrypt.Net.BCrypt.HashPassword(input.Password, workFactor: 12);

        db.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = email,
            PasswordHash = hash
        });

        var (access, accessExp) = tokens.GenerateAccessToken(BuildClaims(userId));

        var (refreshRaw, refreshHash) = tokens.GenerateRefreshToken();
        var refreshExp = DateTime.UtcNow.AddDays(30);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp
        });

        await db.SaveChangesAsync(ct);

        return new AuthPayload(access, accessExp, refreshRaw, refreshExp);
    }

    public async Task<AuthPayload> Login(
        LoginInput input,
        [Service] DataContext db,
        [Service] ITokenService tokens,
        CancellationToken ct)
    {
        var email = input.Email.Trim().ToLowerInvariant();

        var cred = await db.UserCredentials.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (cred is null)
            throw new InvalidCredentialsException();

        if (!BCrypt.Net.BCrypt.Verify(input.Password, cred.PasswordHash))
            throw new InvalidCredentialsException();

        var (access, accessExp) = tokens.GenerateAccessToken(BuildClaims(cred.UserId));

        var (refreshRaw, refreshHash) = tokens.GenerateRefreshToken();
        var refreshExp = DateTime.UtcNow.AddDays(30);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = cred.UserId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp
        });

        await db.SaveChangesAsync(ct);

        return new AuthPayload(access, accessExp, refreshRaw, refreshExp);
    }

    public async Task<AuthPayload> Refresh(
        RefreshInput input,
        [Service] DataContext db,
        [Service] ITokenService tokens,
        CancellationToken ct)
    {
        var oldHash = tokens.HashRefreshToken(input.RefreshToken);

        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == oldHash, ct);
        if (stored is null)
            throw new GraphQLException("Invalid refresh token");

        if (stored.RevokedAtUtc is not null)
            throw new GraphQLException("Refresh token revoked");

        if (stored.ExpiresAtUtc <= DateTime.UtcNow)
            throw new GraphQLException("Refresh token expired");

        // rotation
        var (newRaw, newHash) = tokens.GenerateRefreshToken();
        var newExp = DateTime.UtcNow.AddDays(30);

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = newHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = newHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = newExp
        });

        var (access, accessExp) = tokens.GenerateAccessToken(BuildClaims(stored.UserId));

        await db.SaveChangesAsync(ct);

        return new AuthPayload(access, accessExp, newRaw, newExp);
    }

    public async Task<LogoutPayload> Logout(
        LogoutInput input,
        [Service] DataContext db,
        [Service] ITokenService tokens,
        CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(input.RefreshToken);

        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new LogoutPayload(true);
    }

    private static IReadOnlyList<Claim> BuildClaims(Guid userId) => new List<Claim>
    {
        new(ClaimTypes.UserId, userId.ToString()),
        new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64)
    };
}