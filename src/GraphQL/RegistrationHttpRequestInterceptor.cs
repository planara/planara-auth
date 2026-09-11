using System.Security.Claims;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Auth.Registration;
using Planara.Auth.Services;
using Planara.Common.Auth.Claims;

namespace Planara.Auth.GraphQL;

public class RegistrationHttpRequestInterceptor : DefaultHttpRequestInterceptor
{
    public override async ValueTask OnCreateAsync(
        HttpContext httpContext,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        await base.OnCreateAsync(httpContext, requestExecutor, requestBuilder, cancellationToken);
        
        SetContext(requestBuilder, RegistrationRequestContext.Empty);
        
        var now = DateTime.UtcNow;
        var sessionToken = GetSessionToken(httpContext);
        var jwt = httpContext.Request.Cookies[RegistrationRequest.CookieName];
        
        
        if (string.IsNullOrWhiteSpace(sessionToken) && !IsRegistrationFlow(httpContext))
            return;

        if (string.IsNullOrWhiteSpace(sessionToken) && string.IsNullOrWhiteSpace(jwt))
            return;

        // Services
        var dataContext = httpContext.RequestServices.GetRequiredService<DataContext>();
        var cryptoService = httpContext.RequestServices.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = httpContext.RequestServices.GetRequiredService<ITokenService>();
        
        // 1. Восстановление регистрации по jwt токену из cookie
        RegistrationSession? jwtRegistration = null;
        RegistrationAuthLevel? jwtAuthLevel = null;

        if (!string.IsNullOrWhiteSpace(jwt))
        {
            ClaimsPrincipal? jwtPrincipal = tokenService.ValidateRegistrationToken(jwt);

            if (jwtPrincipal is null)
            {
                RegistrationRequest.DeleteCookie(httpContext);
            }
            else
            {
                // у jwt userId - Id незавершенной регистрации
                var registrationId = jwtPrincipal.GetUserId();
                jwtAuthLevel = jwtPrincipal.GetRegistrationAuthLevel();

                jwtRegistration = await dataContext.RegistrationSessions
                        .FirstOrDefaultAsync(x => 
                            x.Id == registrationId && x.ExpiresAt > now, cancellationToken);

                if (jwtRegistration is null)
                {
                    RegistrationRequest.DeleteCookie(httpContext);
                    jwtAuthLevel = null;
                }
            }
        }
        
        // 2. Восстановление регистрации по токену из url
        RegistrationSession? tokenRegistration = null;

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var sessionTokenHash = cryptoService.HashSessionToken(sessionToken);

            tokenRegistration = await dataContext.RegistrationSessions
                    .FirstOrDefaultAsync(x =>
                        x.SessionTokenHash == sessionTokenHash && x.ExpiresAt > now, cancellationToken);
        }

        // 3. Если jwt есть
        if (jwtRegistration is not null && jwtAuthLevel.HasValue)
        {
            // Если токен существует и указывает на другую живую регистрацию, то у нас не совпадение ключей регистрации
            if (tokenRegistration is not null && tokenRegistration.Id != jwtRegistration.Id)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetCode("REGISTRATION_SESSION_MISMATCH")
                        .SetMessage("Сессия регистрации не совпадает с токеном регистрации.")
                        .Build());
            }

            // Если токен отсутствует, но jwt влидный, то можно выдать новый токен сессии
            if (tokenRegistration is null)
            {
                sessionToken = cryptoService.GenerateSessionToken();
                jwtRegistration.SessionTokenHash = cryptoService.HashSessionToken(sessionToken);

                await dataContext.SaveChangesAsync(cancellationToken);
            }

            SetContext(requestBuilder, CreateContext(jwtRegistration, sessionToken!, jwtAuthLevel.Value));

            return;
        }

        // 4. Токен сессии есть, а jwt нет, тогда регистрация известна, но нужно заново подтвердить почту
        if (tokenRegistration is not null)
        {
            await RegistrationVerification.IssueCodeAsync(
                    tokenRegistration,
                    dataContext,
                    cryptoService,
                    cancellationToken);

            await dataContext.SaveChangesAsync(cancellationToken);

            // Выдача jwt с правами только для подтверждения почты
            var challengeJwt = tokenService
                    .GenerateRegistrationToken(tokenRegistration.Id, RegistrationAuthLevel.Challenge, tokenRegistration.ExpiresAt);

            RegistrationRequest.SetCookie(httpContext, challengeJwt, tokenRegistration.ExpiresAt);

            SetContext(requestBuilder, CreateContext(tokenRegistration, sessionToken!, RegistrationAuthLevel.Challenge));
        }
    }

    private static RegistrationRequestContext CreateContext(
        RegistrationSession registration, 
        string sessionToken,
        RegistrationAuthLevel authLevel)
    {
        return new RegistrationRequestContext(
            RegistrationId: registration.Id,
            Email: registration.Email,
            SessionToken: sessionToken,
            CurrentStep: registration.CurrentStep,
            RequiredStep: RegistrationStateMachine.GetRequiredStep(registration.NextStep, authLevel),
            AuthLevel: authLevel);
    }

    private static bool IsRegistrationFlow(HttpContext context) => 
        context.Request.Headers.TryGetValue(RegistrationRequest.FlowHeader, out var values) && 
        values.FirstOrDefault() == "1";

    private static string? GetSessionToken(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(RegistrationRequest.SessionTokenHeader, out var values))
            return null;

        var token = values.FirstOrDefault();

        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static void SetContext(OperationRequestBuilder builder, RegistrationRequestContext context) =>
        builder.SetGlobalState(RegistrationRequest.ContextKey, context);
}