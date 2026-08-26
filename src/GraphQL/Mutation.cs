using System.Security.Claims;
using System.Text.Json;
using AppAny.HotChocolate.FluentValidation;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;
using Planara.Auth.Requests;
using Planara.Auth.Responses;
using Planara.Auth.Services;
using Planara.Auth.Validators;
using Planara.Common.Exceptions;
using Planara.Common.Kafka;
using Planara.Kafka.Configurations;
using Planara.Common.Auth.Claims;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.GraphQL;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class Mutation(ITokenService tokenService, IHttpContextAccessor http)
{
    /// <summary>
    /// IP-адрес клиента, с которого выполняется текущий запрос
    /// </summary>
    private string? ClientIp =>
        http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// User-Agent клиента, с которого выполняется текущий запрос
    /// </summary>
    private string? UserAgent =>
        http.HttpContext?.Request.Headers.UserAgent.ToString();

    [AllowAnonymous]
    [GraphQLDescription("Вход в аккаунт и выдача новой пары access/refresh токенов")]
    public async Task<AuthResponse> Login(
        [GraphQLDescription("Данные для входа")]
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

        await dataContext.RefreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = cred.UserId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp,
            CreatedByIp = ClientIp,
            UserAgent = UserAgent
        }, cancellationToken);

        await dataContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse{
            AccessToken = access, 
            AccessExpiresAtUtc = accessExp, 
            RefreshToken = refreshRaw, 
            RefreshExpiresAtUtc = refreshExp
        };
    }

    [AllowAnonymous]
    [GraphQLDescription("Обновление access токена по refresh токену")]
    public async Task<AuthResponse> Refresh(
        [GraphQLDescription("Refresh токен, выданный при входе/регистрации")]
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

        await dataContext.RefreshTokens.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = newHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = newExp,
            CreatedByIp = ClientIp,
            UserAgent = UserAgent
        }, cancellationToken);

        var (access, accessExp) = tokenService.GenerateAccessToken(BuildClaims(stored.UserId));

        await dataContext.SaveChangesAsync(cancellationToken);
        
        return new AuthResponse{
            AccessToken = access, 
            AccessExpiresAtUtc = accessExp, 
            RefreshToken = newRaw, 
            RefreshExpiresAtUtc = newExp
        };
    }

    [AllowAnonymous]
    [GraphQLDescription("Выход из аккаунта: отзыв refresh токена")]
    public async Task<LogoutResponse> Logout(
        [GraphQLDescription("Refresh токен, который нужно отозвать")]
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

        return new LogoutResponse { Success = true };
    }
    
    [Authorize]
    [GraphQLDescription("Удаление аккаунта текущего пользователя")]
    public async Task<DeleteAccountResponse> DeleteAccount(
        [Service] DataContext dataContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();

        var credential = await dataContext.UserCredentials
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (credential is null)
            return new DeleteAccountResponse { Success = true };

        var refreshTokens = await dataContext.RefreshTokens
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        dataContext.RefreshTokens.RemoveRange(refreshTokens);

        var kafkaMessage = new UserDeletedMessage { UserId = userId };
        
        await dataContext.OutboxMessages.AddAsync(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.UserDeleted,
            Type = nameof(UserDeletedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(kafkaMessage, KafkaJson.SerializerOptions)
        }, cancellationToken);

        dataContext.UserCredentials.Remove(credential);

        await dataContext.SaveChangesAsync(cancellationToken);

        return new DeleteAccountResponse { Success = true };
    }
    
    [AllowAnonymous]
    [GraphQLDescription("Начало регистрации, ввод почты")]
    public async Task<RegistrationFlowResponse> BeginRegistration(
        [GraphQLDescription("Адрес электронной почты пользователя")]
        [UseFluentValidation, UseValidator<BeginRegistrationRequestValidator>] 
        BeginRegistrationRequest request,
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        
        var registeredUserExists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == email, cancellationToken);
        
        if (registeredUserExists)
            throw new GraphQLException("Пользователь с таким адресом электронной почты уже зарегистрирован.");
        
        // Interceptor уже восстановил регистрацию
        if (registrationContext.HasSession)
        {
            if (!string.Equals(registrationContext.Email, email, StringComparison.Ordinal))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetCode("REGISTRATION_EMAIL_MISMATCH")
                        .SetMessage("В браузере уже активна регистрация с другим адресом электронной почты.")
                        .Build());
            }

            return new RegistrationFlowResponse
            {
                SessionToken = registrationContext.SessionToken!,
                Step = registrationContext.CurrentStep!.Value,
                NextStep = registrationContext.RequiredStep!.Value
            };
        }
        
        var pendingRegistration = await dataContext.RegistrationSessions
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (pendingRegistration is not null && pendingRegistration.ExpiresAt <= DateTime.UtcNow)
        {
            dataContext.RegistrationSessions.Remove(pendingRegistration);

            await dataContext.SaveChangesAsync(cancellationToken);
        }
        
        var existingRegistration = await dataContext.RegistrationSessions
                .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        
        // Восстановление существующей регистрации
        if (existingRegistration is not null)
        {
            var sessionToken = registrationCryptoService.GenerateSessionToken();

            existingRegistration.SessionTokenHash = registrationCryptoService.HashSessionToken(sessionToken);

            await RegistrationVerification
                .IssueCodeAsync(
                    existingRegistration,
                    dataContext,
                    registrationCryptoService,
                    cancellationToken);

            await dataContext.SaveChangesAsync(cancellationToken);

            var registrationJwt = tokenService
                .GenerateRegistrationToken(
                    existingRegistration.Id,
                    RegistrationAuthLevel.Challenge,
                    existingRegistration.ExpiresAt);

            RegistrationRequest.SetCookie(http.HttpContext!, registrationJwt, existingRegistration.ExpiresAt);

            return new RegistrationFlowResponse
            {
                SessionToken = sessionToken,
                Step = existingRegistration.CurrentStep,
                NextStep = RegistrationStep.Code
            };
        }
        
        // Новая регистрация
        var newSessionToken = registrationCryptoService.GenerateSessionToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        
        var registration = new RegistrationSession
        {
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            Email = email,
            SessionTokenHash = registrationCryptoService.HashSessionToken(newSessionToken),
            ExpiresAt = expiresAt.UtcDateTime
        };
        
        var consentVersion = await dataContext.ConsentVersions
            .SingleAsync(x => x.Type == ConsentType.PersonalData && x.IsCurrent, cancellationToken);
        
        registration.Consents.Add(
            new Consent
            {
                ConsentVersionId = consentVersion.Id,
                GivenAt = DateTime.UtcNow,
                IpAddress = ClientIp,
                UserAgent = UserAgent
            });

        await dataContext.RegistrationSessions.AddAsync(registration, cancellationToken);
        
        await RegistrationVerification
            .IssueCodeAsync(
                registration,
                dataContext,
                registrationCryptoService,
                cancellationToken);
        
        await dataContext.SaveChangesAsync(cancellationToken);
        
        var newRegistrationJwt =
            tokenService.GenerateRegistrationToken(registration.Id, RegistrationAuthLevel.Challenge, registration.ExpiresAt);

        RegistrationRequest.SetCookie(http.HttpContext!, newRegistrationJwt, registration.ExpiresAt);
        
        return new RegistrationFlowResponse
        {
            SessionToken = newSessionToken,
            Step = registration.CurrentStep,
            NextStep = registration.NextStep
        };
    }

    /// <summary>
    /// Формирует набор клеймов для JWT access token
    /// </summary>
    /// <param name="userId">Идентификатор пользователя, который будет помещён в клеймы токена</param>
    /// <returns>
    /// Набор клеймов, включающий:
    /// <list type="bullet">
    /// <item><description><c>userId</c> — идентификатор пользователя</description></item>
    /// <item><description><c>jti</c> — уникальный идентификатор токена (JWT ID)</description></item>
    /// <item><description><c>iat</c> — время выпуска токена в формате Unix time (секунды)</description></item>
    /// </list>
    /// </returns>
    private static IReadOnlyList<Claim> BuildClaims(Guid userId) => new List<Claim>
    {
        new(ClaimTypes.UserId, userId.ToString()),
        new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ClaimValueTypes.Integer64)
    };
}