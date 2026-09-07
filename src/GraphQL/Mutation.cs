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
using Planara.Common.Database.Domain;
using Planara.Common.Enums;
using Planara.Common.GraphQL.Attributes;
using Planara.Common.Kafka.Messages.Auth;
using Planara.Common.Kafka.Messages.Notifications;
using Planara.Common.Kafka.Messages.Privacy;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.GraphQL;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class Mutation(ITokenService tokenService, IHttpContextAccessor http)
{
    /// <summary>
    /// IP-адрес клиента, с которого выполняется текущий запрос
    /// </summary>
    private string? ClientIp => http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// User-Agent клиента, с которого выполняется текущий запрос
    /// </summary>
    private string? UserAgent => http.HttpContext?.Request.Headers.UserAgent.ToString();

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

        dataContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = cred.UserId,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = refreshExp,
            CreatedByIp = ClientIp,
            UserAgent = UserAgent
        });

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

        dataContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = newHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = newExp,
            CreatedByIp = ClientIp,
            UserAgent = UserAgent
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
        
        dataContext.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.UserDeleted,
            Type = nameof(UserDeletedMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(kafkaMessage, KafkaJson.SerializerOptions)
        });

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
                throw new GraphQLException(ErrorBuilder.New()
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
                .IssueCodeAsync(existingRegistration, dataContext, registrationCryptoService, cancellationToken);

            await dataContext.SaveChangesAsync(cancellationToken);

            var registrationJwt = tokenService
                .GenerateRegistrationToken(existingRegistration.Id, RegistrationAuthLevel.Challenge, existingRegistration.ExpiresAt);

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
        
        var now = DateTime.UtcNow;
        var expiresAt = DateTimeOffset.UtcNow.AddHours(2);
        
        var registration = new RegistrationSession
        {
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            Email = email,
            SessionTokenHash = registrationCryptoService.HashSessionToken(newSessionToken),
            ExpiresAt = expiresAt.UtcDateTime
        };
        
        dataContext.RegistrationSessions.Add(registration);

        var message = new ConsentGrantRequestedMessage
        {
            RequestId = Guid.NewGuid(),
            RegistrationId = registration.Id,
            Type = ConsentType.PersonalData,
            ConsentVersionId = request.ConsentVersionId,
            GivenAt = now,
            ExpiresAt = registration.ExpiresAt,
            IpAddress = ClientIp,
            UserAgent = UserAgent
        };
        
        dataContext.OutboxMessages.Add(
            new OutboxMessage
            {
                TopicKey = KafkaTopicKeys.ConsentGrantRequested,
                Type = nameof(ConsentGrantRequestedMessage),
                Key = registration.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(message, KafkaJson.SerializerOptions)
            });
        
        await RegistrationVerification.IssueCodeAsync(registration, dataContext, registrationCryptoService, cancellationToken);
        
        await dataContext.SaveChangesAsync(cancellationToken);
        
        var newRegistrationJwt = tokenService.GenerateRegistrationToken(
            registration.Id, RegistrationAuthLevel.Challenge, registration.ExpiresAt);

        RegistrationRequest.SetCookie(http.HttpContext!, newRegistrationJwt, registration.ExpiresAt);
        
        return new RegistrationFlowResponse
        {
            SessionToken = newSessionToken,
            Step = registration.CurrentStep,
            NextStep = registration.NextStep
        };
    }
    
    [AllowAnonymous]
    [UseRegistrationStep(RegistrationStep.Code)]
    [GraphQLDescription("Подтверждение адреса электронной почты")]
    public async Task<RegistrationFlowResponse> VerifyRegistrationCode(
        [GraphQLDescription("Код подтверждения из письма")]
        [UseFluentValidation, UseValidator<VerifyRegistrationCodeRequestValidator>]
        VerifyRegistrationCodeRequest request,
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
    
        var registrationId = registrationContext.RegistrationId!.Value;
    
        var registration = await dataContext.RegistrationSessions
            .SingleOrDefaultAsync(x => x.Id == registrationId && x.ExpiresAt > now, cancellationToken);
    
        var verification = await dataContext.RegistrationEmailVerifications
            .SingleOrDefaultAsync(x => x.RegistrationSessionId == registration!.Id, cancellationToken);
    
        if (verification is null)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_CODE_NOT_FOUND")
                .SetMessage("Код подтверждения отсутствует.")
                .Build());
        }
    
        if (verification.ExpiresAt <= now)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_CODE_EXPIRED")
                .SetMessage("Срок действия кода подтверждения истёк.")
                .Build());
        }
    
        if (verification.Attempts >= RegistrationVerification.MaxAttempts)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_CODE_ATTEMPTS_EXCEEDED")
                .SetMessage("Превышено количество попыток ввода кода.")
                .Build());
        }
    
        var codeValid = registrationCryptoService.VerifyCode(request.Code, verification.CodeHash);
    
        if (!codeValid)
        {
            verification.Attempts++;
    
            await dataContext.SaveChangesAsync(cancellationToken);
    
            var attemptsLeft = Math.Max(0, RegistrationVerification.MaxAttempts - verification.Attempts);
    
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode(attemptsLeft == 0 ? "REGISTRATION_CODE_ATTEMPTS_EXCEEDED" : "REGISTRATION_CODE_INVALID")
                .SetMessage(attemptsLeft == 0 ? "Превышено количество попыток ввода кода." : "Неверный код подтверждения.")
                .SetExtension("attemptsLeft", attemptsLeft)
                .Build());
        }
        
        RegistrationStateMachine.CompleteEmailVerification(registration!);
        dataContext.RegistrationEmailVerifications.Remove(verification);
    
        await dataContext.SaveChangesAsync(cancellationToken);
        
        var authorizedJwt = tokenService.GenerateRegistrationToken(registration!.Id, RegistrationAuthLevel.Authorized, registration.ExpiresAt);
    
        RegistrationRequest.SetCookie(http.HttpContext!, authorizedJwt, registration.ExpiresAt);
    
        return new RegistrationFlowResponse
        {
            SessionToken = registrationContext.SessionToken!,
            Step = registration.CurrentStep,
            NextStep = RegistrationStateMachine.GetRequiredStep(registration.NextStep, RegistrationAuthLevel.Authorized)
        };
    }
    
    [AllowAnonymous]
    [UseRegistrationStep(RegistrationStep.Code)]
    [GraphQLDescription("Повторная отправка кода подтверждения")]
    public async Task<bool> ResendRegistrationCode(
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
    
        var registration = await dataContext.RegistrationSessions.SingleOrDefaultAsync(x => 
            x.Id == registrationContext.RegistrationId!.Value && x.ExpiresAt > now, cancellationToken);
    
        var verification = await dataContext.RegistrationEmailVerifications.SingleOrDefaultAsync(x => 
            x.RegistrationSessionId == registration!.Id, cancellationToken);
    
        if (verification is not null && verification.ResendAvailableAt > now)
        {
            var retryAfter = Math.Max(1, (int)Math.Ceiling((verification.ResendAvailableAt - now).TotalSeconds));
    
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_CODE_RESEND_COOLDOWN")
                .SetMessage("Код уже был недавно отправлен.")
                .SetExtension("retryAfter", retryAfter)
                .Build());
        }
    
        await RegistrationVerification.IssueCodeAsync(registration!, dataContext, registrationCryptoService, cancellationToken);
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return true;
    }
    
    [AllowAnonymous]
    [UseRegistrationStep(RegistrationStep.Password)]
    [GraphQLDescription("Установка пароля при регистрации")]
    public async Task<RegistrationFlowResponse> SetRegistrationPassword(
        [GraphQLDescription("Пароль пользователя")]
        [UseFluentValidation, UseValidator<SetRegistrationPasswordRequestValidator>]
        SetRegistrationPasswordRequest request,
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var registration = await dataContext.RegistrationSessions.SingleOrDefaultAsync(x => 
            x.Id == registrationContext.RegistrationId!.Value && x.ExpiresAt > DateTime.UtcNow, cancellationToken);

        registration!.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        RegistrationStateMachine.CompleteStep(registration, RegistrationStep.Password);

        await dataContext.SaveChangesAsync(cancellationToken);

        return new RegistrationFlowResponse
        {
            SessionToken = registrationContext.SessionToken!,
            Step = registration.CurrentStep,
            NextStep = registration.NextStep
        };
    }
    
    [AllowAnonymous]
    [UseRegistrationStep(RegistrationStep.Personal)]
    [ConsentRequired(ConsentType.PersonalData)]
    [GraphQLDescription("Завершение регистрации пользователя")]
    public async Task<AuthResponse> CompleteRegistration(
        [GraphQLDescription("Персональные данные пользователя")]
        [UseFluentValidation, UseValidator<SetRegistrationPersonalRequestValidator>]
        SetRegistrationPersonalRequest request,
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [LocalState(RegistrationRequest.SessionKey)] RegistrationSession registration,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
    
        registration.Name = NormalizeOptional(request.Name);
        registration.Surname = NormalizeOptional(request.Surname);
    
        if (string.IsNullOrWhiteSpace(registration.PasswordHash))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_PASSWORD_MISSING")
                .SetMessage("Пароль регистрации отсутствует.")
                .Build());
        }
        
        var emailExists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == registration.Email, cancellationToken);
    
        if (emailExists)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_ALREADY_REGISTERED")
                .SetMessage("Пользователь с таким адресом электронной почты уже зарегистрирован.")
                .Build());
        }
    
        var userId = Guid.NewGuid();
    
        var credential = new UserCredential
        {
            UserId = userId,
            Email = registration.Email!,
            IsEmailConfirmed = true,
            PasswordHash = registration.PasswordHash
        };
    
        dataContext.UserCredentials.Add(credential);
        
        var consents = await dataContext.UserConsentProjections
            .Where(x => x.UserId == registration.Id && x.IsGranted)
            .ToListAsync(cancellationToken);

        var consentOutboxMessages = consents.Select(consent =>
        {
            var consentMessage = new ConsentGrantRequestedMessage
            {
                RequestId = Guid.NewGuid(),
                UserId = userId,
                Type = consent.Type,
                ConsentVersionId = consent.ConsentVersionId,
                GivenAt = consent.GrantedAt,
                IpAddress = ClientIp,
                UserAgent = UserAgent
            };

            return new OutboxMessage
            {
                TopicKey = KafkaTopicKeys.ConsentGrantRequested,
                Type = nameof(ConsentGrantRequestedMessage),
                Key = userId.ToString("N"),
                PayloadJson = JsonSerializer.Serialize(
                    consentMessage,
                    KafkaJson.SerializerOptions)
            };
        });

        dataContext.OutboxMessages.AddRange(consentOutboxMessages);
        dataContext.UserConsentProjections.RemoveRange(consents);
        
        var (refreshRaw, refreshHash) = tokenService.GenerateRefreshToken();
        var refreshExpiresAt = now.AddDays(30);
    
        dataContext.RefreshTokens.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = refreshHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = refreshExpiresAt,
                CreatedByIp = ClientIp,
                UserAgent = UserAgent
            });
        
        var userCreatedMessage = new UserCreatedMessage
            {
                UserId = userId,
                Email = registration.Email!,
                Name = registration.Name,
                Surname = registration.Surname
            };
    
        dataContext.OutboxMessages.Add(new OutboxMessage
            {
                TopicKey = KafkaTopicKeys.UserCreated,
                Type = nameof(UserCreatedMessage),
                Key = userId.ToString("N"),
                PayloadJson = JsonSerializer.Serialize(userCreatedMessage, KafkaJson.SerializerOptions)
            });
        
        dataContext.RegistrationSessions.Remove(registration);
        await dataContext.SaveChangesAsync(cancellationToken);
        
        RegistrationRequest.DeleteCookie(http.HttpContext!);
        var (accessToken, accessExpiresAt) = tokenService.GenerateAccessToken(BuildClaims(userId));
    
        return new AuthResponse
        {
            AccessToken = accessToken,
            AccessExpiresAtUtc = accessExpiresAt,
            RefreshToken = refreshRaw,
            RefreshExpiresAtUtc = refreshExpiresAt
        };
    }
    
    [Authorize]
    [GraphQLDescription("Изменение пароля текущего пользователя")]
    public async Task<bool> ChangePassword(
        [GraphQLDescription("Данные для изменения пароля")]
        [UseFluentValidation, UseValidator<ChangePasswordRequestValidator>]
        ChangePasswordRequest request,
        ClaimsPrincipal user,
        [Service] DataContext dataContext,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();

        var credential = await dataContext.UserCredentials
            .SingleAsync(x => x.UserId == userId, cancellationToken);

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, credential.PasswordHash))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("INVALID_CURRENT_PASSWORD")
                .SetMessage("Текущий пароль указан неверно.")
                .Build());
        }

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, credential.PasswordHash))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("PASSWORD_NOT_CHANGED")
                .SetMessage("Новый пароль должен отличаться от текущего.")
                .Build());
        }

        credential.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);

        await dataContext.SaveChangesAsync(cancellationToken);

        return true;
    }
    
    [Authorize]
    [GraphQLDescription("Начало изменения адреса электронной почты")]
    public async Task<bool> ChangeEmail(
        [GraphQLDescription("Новый адрес электронной почты")]
        [UseFluentValidation, UseValidator<ChangeEmailRequestValidator>]
        ChangeEmailRequest request,
        ClaimsPrincipal user,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var email = request.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
    
        var credential = await dataContext.UserCredentials
            .SingleAsync(x => x.UserId == userId, cancellationToken);
    
        if (string.Equals(credential.Email, email, StringComparison.Ordinal))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_NOT_CHANGED")
                .SetMessage("Новый адрес электронной почты совпадает с текущим.")
                .Build());
        }
    
        var emailExists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == email && x.UserId != userId, cancellationToken);
    
        if (emailExists)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_ALREADY_REGISTERED")
                .SetMessage("Пользователь с таким адресом электронной почты уже зарегистрирован.")
                .Build());
        }
    
        var code = registrationCryptoService.GenerateCode();
    
        var verification = await dataContext.UserEmailVerifications
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Type == EmailVerificationType.EmailChange, cancellationToken);
    
        if (verification is null)
        {
            verification = new UserEmailVerification
            {
                UserId = userId,
                Type = EmailVerificationType.EmailChange,
                Email = email,
                CodeHash = registrationCryptoService.HashCode(code),
                Attempts = 0,
                ExpiresAt = now.AddMinutes(15),
                ResendAvailableAt = now.AddMinutes(1)
            };

            dataContext.UserEmailVerifications.Add(verification);
        }
        else
        {
            verification.Email = email;
            verification.CodeHash = registrationCryptoService.HashCode(code);
            verification.Attempts = 0;
            verification.ExpiresAt = now.AddMinutes(15);
            verification.ResendAvailableAt = now.AddMinutes(1);
        }
    
        var message = new EmailConfirmationMessage
        {
            Id = Guid.NewGuid(),
            Email = email,
            Code = code
        };
    
        dataContext.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.EmailConfirmation,
            Type = nameof(EmailConfirmationMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(message, KafkaJson.SerializerOptions)
        });
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return true;
    }
    
    [Authorize]
    [GraphQLDescription("Подтверждение изменения адреса электронной почты")]
    public async Task<bool> ConfirmEmailChange(
        [GraphQLDescription("Код подтверждения")]
        [UseFluentValidation, UseValidator<ConfirmEmailChangeRequestValidator>]
        ConfirmEmailChangeRequest request,
        ClaimsPrincipal user,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var now = DateTime.UtcNow;
    
        var verification = await dataContext.UserEmailVerifications
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.Type == EmailVerificationType.EmailChange,
                cancellationToken);
    
        if (verification is null)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CHANGE_NOT_FOUND")
                .SetMessage("Запрос на изменение адреса электронной почты отсутствует.")
                .Build());
        }

        if (verification.ExpiresAt <= now)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CHANGE_CODE_EXPIRED")
                .SetMessage("Срок действия кода подтверждения истёк.")
                .Build());
        }

        if (verification.Attempts >= RegistrationVerification.MaxAttempts)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CHANGE_CODE_ATTEMPTS_EXCEEDED")
                .SetMessage("Превышено количество попыток ввода кода.")
                .Build());
        }
    
        if (!registrationCryptoService.VerifyCode(request.Code, verification.CodeHash))
        {
            verification.Attempts++;
    
            await dataContext.SaveChangesAsync(cancellationToken);
    
            var attemptsLeft = Math.Max(0, RegistrationVerification.MaxAttempts - verification.Attempts);
    
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode(attemptsLeft == 0 ? "EMAIL_CHANGE_CODE_ATTEMPTS_EXCEEDED" : "EMAIL_CHANGE_CODE_INVALID")
                .SetMessage(attemptsLeft == 0 ? "Превышено количество попыток ввода кода." : "Неверный код подтверждения.")
                .SetExtension("attemptsLeft", attemptsLeft)
                .Build());
        }
    
        var emailExists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == verification.Email && x.UserId != userId, cancellationToken);
    
        if (emailExists)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_ALREADY_REGISTERED")
                .SetMessage("Пользователь с таким адресом электронной почты уже зарегистрирован.")
                .Build());
        }
    
        var credential = await dataContext.UserCredentials
            .SingleAsync(x => x.UserId == userId, cancellationToken);
    
        credential.Email = verification.Email;
        credential.IsEmailConfirmed = true;
    
        dataContext.UserEmailVerifications.Remove(verification);
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return true;
    }
    
    [AllowAnonymous]
    [UseRegistrationStep(RegistrationStep.Code)]
    [GraphQLDescription("Изменение адреса электронной почты во время регистрации")]
    public async Task<RegistrationFlowResponse> ChangeRegistrationEmail(
        [GraphQLDescription("Новый адрес электронной почты")]
        [UseFluentValidation, UseValidator<ChangeRegistrationEmailRequestValidator>]
        ChangeRegistrationEmailRequest request,
        [GlobalState(RegistrationRequest.ContextKey)] RegistrationRequestContext registrationContext,
        [LocalState(RegistrationRequest.SessionKey)] RegistrationSession registration,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
    
        if (string.Equals(registration.Email, email, StringComparison.Ordinal))
        {
            return new RegistrationFlowResponse
            {
                SessionToken = registrationContext.SessionToken!,
                Step = registration.CurrentStep,
                NextStep = registration.NextStep
            };
        }
    
        var registeredUserExists = await dataContext.UserCredentials
            .AnyAsync(x => x.Email == email, cancellationToken);
    
        if (registeredUserExists)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_ALREADY_REGISTERED")
                .SetMessage("Пользователь с таким адресом электронной почты уже зарегистрирован.")
                .Build());
        }
    
        var pendingRegistrationExists = await dataContext.RegistrationSessions
            .AnyAsync(x =>
                x.Email == email &&
                x.Id != registration.Id &&
                x.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
    
        if (pendingRegistrationExists)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("REGISTRATION_EMAIL_ALREADY_IN_USE")
                .SetMessage("С этим адресом электронной почты уже выполняется регистрация.")
                .Build());
        }
    
        registration.Email = email;
    
        await RegistrationVerification.IssueCodeAsync(
            registration,
            dataContext,
            registrationCryptoService,
            cancellationToken);
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return new RegistrationFlowResponse
        {
            SessionToken = registrationContext.SessionToken!,
            Step = registration.CurrentStep,
            NextStep = registration.NextStep
        };
    }
    
    [Authorize]
    [GraphQLDescription("Отправка кода подтверждения текущего адреса электронной почты")]
    public async Task<bool> RequestEmailConfirmation(
        ClaimsPrincipal user,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var now = DateTime.UtcNow;
    
        var credential = await dataContext.UserCredentials
            .SingleAsync(x => x.UserId == userId, cancellationToken);
    
        if (credential.IsEmailConfirmed)
            return true;
    
        var verification = await dataContext.UserEmailVerifications
            .SingleOrDefaultAsync(
                x => x.UserId == userId &&
                     x.Type == EmailVerificationType.EmailConfirmation,
                cancellationToken);
    
        if (verification is not null &&
            verification.ResendAvailableAt > now)
        {
            var retryAfter = Math.Max(
                1,
                (int)Math.Ceiling(
                    (verification.ResendAvailableAt - now).TotalSeconds));
    
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CONFIRMATION_RESEND_COOLDOWN")
                .SetMessage("Код уже был недавно отправлен.")
                .SetExtension("retryAfter", retryAfter)
                .Build());
        }
    
        var code = registrationCryptoService.GenerateCode();
    
        if (verification is null)
        {
            verification = new UserEmailVerification
            {
                UserId = userId,
                Type = EmailVerificationType.EmailConfirmation,
                Email = credential.Email,
                CodeHash = registrationCryptoService.HashCode(code),
                Attempts = 0,
                ExpiresAt = now.AddMinutes(15),
                ResendAvailableAt = now.AddMinutes(1)
            };
    
            dataContext.UserEmailVerifications.Add(verification);
        }
        else
        {
            verification.Email = credential.Email;
            verification.CodeHash = registrationCryptoService.HashCode(code);
            verification.Attempts = 0;
            verification.ExpiresAt = now.AddMinutes(15);
            verification.ResendAvailableAt = now.AddMinutes(1);
        }
    
        var message = new EmailConfirmationMessage
        {
            Id = Guid.NewGuid(),
            Email = credential.Email,
            Code = code
        };
    
        dataContext.OutboxMessages.Add(new OutboxMessage
        {
            TopicKey = KafkaTopicKeys.EmailConfirmation,
            Type = nameof(EmailConfirmationMessage),
            Key = userId.ToString("N"),
            PayloadJson = JsonSerializer.Serialize(
                message,
                KafkaJson.SerializerOptions)
        });
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return true;
    }
    
    [Authorize]
    [GraphQLDescription("Подтверждение текущего адреса электронной почты")]
    public async Task<bool> ConfirmEmail(
        [GraphQLDescription("Код подтверждения")]
        [UseFluentValidation, UseValidator<ConfirmEmailRequestValidator>]
        ConfirmEmailRequest request,
        ClaimsPrincipal user,
        [Service] DataContext dataContext,
        [Service] IRegistrationCryptoService registrationCryptoService,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();
        var now = DateTime.UtcNow;
    
        var credential = await dataContext.UserCredentials
            .SingleAsync(x => x.UserId == userId, cancellationToken);
    
        if (credential.IsEmailConfirmed)
            return true;
    
        var verification = await dataContext.UserEmailVerifications
            .SingleOrDefaultAsync(
                x => x.UserId == userId &&
                     x.Type == EmailVerificationType.EmailConfirmation,
                cancellationToken);
    
        if (verification is null)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CONFIRMATION_NOT_FOUND")
                .SetMessage("Код подтверждения отсутствует.")
                .Build());
        }
    
        if (verification.ExpiresAt <= now)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CONFIRMATION_CODE_EXPIRED")
                .SetMessage("Срок действия кода подтверждения истёк.")
                .Build());
        }
    
        if (verification.Attempts >= RegistrationVerification.MaxAttempts)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CONFIRMATION_CODE_ATTEMPTS_EXCEEDED")
                .SetMessage("Превышено количество попыток ввода кода.")
                .Build());
        }
    
        if (!registrationCryptoService.VerifyCode(
                request.Code,
                verification.CodeHash))
        {
            verification.Attempts++;
    
            await dataContext.SaveChangesAsync(cancellationToken);
    
            var attemptsLeft = Math.Max(
                0,
                RegistrationVerification.MaxAttempts - verification.Attempts);
    
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode(attemptsLeft == 0
                    ? "EMAIL_CONFIRMATION_CODE_ATTEMPTS_EXCEEDED"
                    : "EMAIL_CONFIRMATION_CODE_INVALID")
                .SetMessage(attemptsLeft == 0
                    ? "Превышено количество попыток ввода кода."
                    : "Неверный код подтверждения.")
                .SetExtension("attemptsLeft", attemptsLeft)
                .Build());
        }
    
        // За время между отправкой и подтверждением почта не должна была измениться
        if (!string.Equals(
                credential.Email,
                verification.Email,
                StringComparison.Ordinal))
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetCode("EMAIL_CONFIRMATION_EMAIL_CHANGED")
                .SetMessage("Адрес электронной почты был изменён. Запросите новый код подтверждения.")
                .Build());
        }
    
        credential.IsEmailConfirmed = true;
    
        dataContext.UserEmailVerifications.Remove(verification);
    
        await dataContext.SaveChangesAsync(cancellationToken);
    
        return true;
    }

    /// <summary>
    /// Нормализация необязательного строкового значения
    /// </summary>
    /// <param name="value">Исходное строковое значение</param>
    /// <returns>
    /// Значение без начальных и конечных пробелов либо <see langword="null"/>,
    /// если исходное значение отсутствует или содержит только пробелы
    /// </returns>
    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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