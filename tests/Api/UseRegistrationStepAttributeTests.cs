using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;
using Planara.Auth.Services;

namespace Planara.Auth.Tests.Api;

public class UseRegistrationStepAttributeTests : BaseApiTest
{
    public UseRegistrationStepAttributeTests(ApiTestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task RegistrationStep_WhenSessionIsMissing_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        Client.DefaultRequestHeaders.Remove(RegistrationRequest.SessionTokenHeader);
        Client.DefaultRequestHeaders.Remove("Cookie");

        const string mutation = """
                                mutation ($request: SetRegistrationPasswordRequestInput!) {
                                  setRegistrationPassword(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                password = "Password1!",
                confirmPassword = "Password1!"
            }
        });

        doc.GetErrors().Should().NotBeNull();
    }

    [Fact]
    public async Task RegistrationStep_WhenChallengeSessionCallsPasswordStep_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();

        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "wrong-step-challenge@planara.test",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Email,
            NextStep = RegistrationStep.Code,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        Context.RegistrationSessions.Add(registration);

        await Context.SaveChangesAsync();

        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            registration.ExpiresAt);

        Client.DefaultRequestHeaders.Remove(RegistrationRequest.SessionTokenHeader);
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");

        const string mutation = """
                                mutation ($request: SetRegistrationPasswordRequestInput!) {
                                  setRegistrationPassword(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                password = "Password1!",
                confirmPassword = "Password1!"
            }
        });

        doc.GetErrors().Should().NotBeNull();

        Context.ChangeTracker.Clear();

        var stored = await Context.RegistrationSessions.AsNoTracking().SingleAsync();

        stored.PasswordHash.Should().BeNull();
        stored.NextStep.Should().Be(RegistrationStep.Code);
    }

    [Fact]
    public async Task RegistrationStep_WhenAuthorizedSessionCallsCodeStep_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var cryptoService = Scope.ServiceProvider.GetRequiredService<IRegistrationCryptoService>();
        var tokenService = Scope.ServiceProvider.GetRequiredService<ITokenService>();
        var sessionToken = cryptoService.GenerateSessionToken();

        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = "wrong-step-authorized@planara.test",
            SessionTokenHash = cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Code,
            NextStep = RegistrationStep.Password,
            IsEmailConfirmed = true,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        Context.RegistrationSessions.Add(registration);

        await Context.SaveChangesAsync();

        var jwt = tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);

        Client.DefaultRequestHeaders.Remove(RegistrationRequest.SessionTokenHeader);
        Client.DefaultRequestHeaders.Remove("Cookie");
        Client.DefaultRequestHeaders.TryAddWithoutValidation(RegistrationRequest.SessionTokenHeader, sessionToken);
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{RegistrationRequest.CookieName}={jwt}");

        const string mutation = """
                                mutation ($request: VerifyRegistrationCodeRequestInput!) {
                                  verifyRegistrationCode(request: $request) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;

        using var doc = await Client.PostAsync(mutation, new
        {
            request = new
            {
                code = "1234"
            }
        });

        doc.GetErrors().Should().NotBeNull();
    }
    
    [Fact]
    public async Task RegistrationStep_WhenSessionIsExpired_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var registrationId = Guid.NewGuid();

        Context.RegistrationSessions.Add(new RegistrationSession
        {
            Id = registrationId,
            Email = "expired-step@planara.test",
            SessionTokenHash = "session-token-hash",
            CurrentStep = RegistrationStep.Code,
            NextStep = RegistrationStep.Password,
            IsEmailConfirmed = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await Context.SaveChangesAsync();

        var registrationContext = new RegistrationRequestContext(
            registrationId,
            "expired-step@planara.test",
            "session-token",
            RegistrationStep.Code,
            RegistrationStep.Password,
            RegistrationAuthLevel.Authorized);

        var executor = await Scope.ServiceProvider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();

        const string mutation = """
                                mutation {
                                  setRegistrationPassword(
                                    request: {
                                      password: "Password1!"
                                      confirmPassword: "Password1!"
                                    }
                                  ) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;

        var request = OperationRequestBuilder.New()
            .SetDocument(mutation)
            .SetGlobalState(RegistrationRequest.ContextKey, registrationContext)
            .Build();

        var result = await executor.ExecuteAsync(request);

        var json = result.ToJson();

        json.Should().Contain("REGISTRATION_SESSION_EXPIRED");
        json.Should().Contain("Сессия регистрации отсутствует или истекла.");
    }
    
    [Fact]
    public async Task RegistrationStep_WhenContextIsMissing_ReturnsError()
    {
        await DbTestUtils.ResetAuthDbAsync(Context);

        var executor = await Scope.ServiceProvider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync();

        const string mutation = """
                                mutation {
                                  setRegistrationPassword(
                                    request: {
                                      password: "Password1!"
                                      confirmPassword: "Password1!"
                                    }
                                  ) {
                                    sessionToken
                                    step
                                    nextStep
                                  }
                                }
                                """;

        var request = OperationRequestBuilder.New().SetDocument(mutation).Build();

        var result = await executor.ExecuteAsync(request);

        var json = result.ToJson();

        json.Should().Contain("REGISTRATION_SESSION_REQUIRED");
        json.Should().Contain("Сессия регистрации отсутствует.");
    }
}