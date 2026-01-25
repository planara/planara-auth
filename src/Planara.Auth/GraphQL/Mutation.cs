using System.Security.Claims;
using AppAny.HotChocolate.FluentValidation;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Auth.Requests;
using Planara.Auth.Responses;
using Planara.Auth.Services;
using Planara.Auth.Validators;
using Planara.Common.Exceptions;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;
using LoginRequest = Microsoft.AspNetCore.Identity.Data.LoginRequest;
using RefreshRequest = Microsoft.AspNetCore.Identity.Data.RefreshRequest;
using RegisterRequest = Microsoft.AspNetCore.Identity.Data.RegisterRequest;

namespace Planara.Auth.GraphQL;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class Mutation(ITokenService tokenService)
{
    public async Task<AuthResponse> Register(
        [UseFluentValidation, UseValidator<RegisterRequestValidator>]
        RegisterRequest request,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var exists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == email, cancellationToken);
        
        if (exists)
            throw new GraphQLException("Email already registered");

        var userId = Guid.NewGuid();
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        dataContext.UserCredentials.Add(new UserCredential
        {
            UserId = userId,
            Email = email,
            PasswordHash = hash
        });

        var (access, accessExp) = tokenService.GenerateAccessToken(BuildClaims(userId));
        var (refreshRaw, refreshHash) = tokenService.GenerateRefreshToken();
        var refreshExp = DateTime.UtcNow.AddDays(30);

        dataContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp
        });

        await dataContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse{
            AccessToken = access, 
            AccessExpiresAtUtc = accessExp, 
            RefreshToken = refreshRaw, 
            RefreshExpiresAtUtc = refreshExp
        };
    }

    public async Task<AuthResponse> Login(
        [UseFluentValidation, UseValidator<LoginRequestValidator>]
        LoginRequest login,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var email = login.Email.Trim().ToLowerInvariant();

        var cred = await dataContext.UserCredentials
            .SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        
        if (cred is null)
            throw new InvalidCredentialsException();

        if (!BCrypt.Net.BCrypt.Verify(login.Password, cred.PasswordHash))
            throw new InvalidCredentialsException();

        var (access, accessExp) = tokenService.GenerateAccessToken(BuildClaims(cred.UserId));

        var (refreshRaw, refreshHash) = tokenService.GenerateRefreshToken();
        var refreshExp = DateTime.UtcNow.AddDays(30);

        dataContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = cred.UserId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp
        });

        await dataContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse{
            AccessToken = access, 
            AccessExpiresAtUtc = accessExp, 
            RefreshToken = refreshRaw, 
            RefreshExpiresAtUtc = refreshExp
        };
    }

    public async Task<AuthResponse> Refresh(
        [UseFluentValidation, UseValidator<RefreshRequestValidator>]
        RefreshRequest request,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var oldHash = tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await dataContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == oldHash, cancellationToken);
        
        if (stored is null)
            throw new GraphQLException("Invalid refresh token");

        if (stored.RevokedAtUtc is not null)
            throw new GraphQLException("Refresh token revoked");

        if (stored.ExpiresAtUtc <= DateTime.UtcNow)
            throw new GraphQLException("Refresh token expired");
        
        var (newRaw, newHash) = tokenService.GenerateRefreshToken();
        var newExp = DateTime.UtcNow.AddDays(30);

        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = newHash;

        dataContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = newHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = newExp
        });

        var (access, accessExp) = tokenService.GenerateAccessToken(BuildClaims(stored.UserId));

        await dataContext.SaveChangesAsync(cancellationToken);
        
        return new AuthResponse{
            AccessToken = access, 
            AccessExpiresAtUtc = accessExp, 
            RefreshToken = newRaw, 
            RefreshExpiresAtUtc = newExp
        };
    }

    public async Task<LogoutResponse> Logout(
        [UseFluentValidation, UseValidator<LogoutRequestValidator>]
        LogoutRequest request,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);

        var stored = await dataContext.RefreshTokens
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await dataContext.SaveChangesAsync(cancellationToken);
        }

        return new LogoutResponse { Success =  true };
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