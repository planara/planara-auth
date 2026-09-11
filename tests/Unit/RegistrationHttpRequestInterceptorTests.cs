using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Data.Domain;
using Planara.Auth.Data.Enums;
using Planara.Auth.GraphQL;
using Planara.Auth.Registration;
using Planara.Auth.Services;

namespace Planara.Auth.Tests.Unit;

public class RegistrationHttpRequestInterceptorTests : BaseApiTest
{
    private readonly RegistrationHttpRequestInterceptor _sut = new();
    private readonly IRegistrationCryptoService _cryptoService;
    private readonly ITokenService _tokenService;

    public RegistrationHttpRequestInterceptorTests(ApiTestWebAppFactory factory) : base(factory)
    {
        _cryptoService = Scope.ServiceProvider
            .GetRequiredService<IRegistrationCryptoService>();

        _tokenService = Scope.ServiceProvider
            .GetRequiredService<ITokenService>();
    }

    [Fact]
    public async Task OnCreateAsync_NoSessionAndNoFlow_ReturnsEarly()
    {
        var context = CreateHttpContext();
        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCreateAsync_InvalidFlowHeaderAndNoSession_ReturnsEarly()
    {
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.FlowHeader] = "0";

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCreateAsync_FlowWithoutTokens_ReturnsEarly()
    {
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.FlowHeader] = "1";

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCreateAsync_WhitespaceSessionToken_ReturnsEarly()
    {
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.SessionTokenHeader] = "   ";

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCreateAsync_UnknownSessionToken_DoesNothing()
    {
        var context = CreateHttpContext();
        context.Request.Headers[RegistrationRequest.SessionTokenHeader] = "unknown-session-token";

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task OnCreateAsync_InvalidJwt_DeletesCookie()
    {
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.FlowHeader] = "1";
        SetRequestCookie(context, RegistrationRequest.CookieName, "invalid-jwt");

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie
            .ToString()
            .Should()
            .Contain(RegistrationRequest.CookieName);
    }

    [Fact]
    public async Task OnCreateAsync_JwtForExpiredRegistration_DeletesCookie()
    {
        var (registration, _) = await CreateRegistrationAsync(expiresAt: DateTime.UtcNow.AddMinutes(-5));
        var jwt = _tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Challenge,
            DateTime.UtcNow.AddMinutes(10));
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.FlowHeader] = "1";
        SetRequestCookie(context, RegistrationRequest.CookieName, jwt);

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        context.Response.Headers.SetCookie
            .ToString()
            .Should()
            .Contain(RegistrationRequest.CookieName);
    }

    [Fact]
    public async Task OnCreateAsync_ValidJwtWithoutSessionToken_GeneratesNewSessionToken()
    {
        var (registration, _) = await CreateRegistrationAsync();
        var previousHash = registration.SessionTokenHash;
        var jwt = _tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.FlowHeader] = "1";
        SetRequestCookie(context, RegistrationRequest.CookieName, jwt);

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        registration.SessionTokenHash
            .Should()
            .NotBe(previousHash);
    }

    [Fact]
    public async Task OnCreateAsync_ValidJwtAndMatchingSession_UsesExistingRegistration()
    {
        var (registration, sessionToken) = await CreateRegistrationAsync();
        var previousHash = registration.SessionTokenHash;
        var jwt = _tokenService.GenerateRegistrationToken(
            registration.Id,
            RegistrationAuthLevel.Authorized,
            registration.ExpiresAt);

        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.SessionTokenHeader] = $"  {sessionToken}  ";

        SetRequestCookie(context, RegistrationRequest.CookieName, jwt);

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        registration.SessionTokenHash
            .Should()
            .Be(previousHash);
    }

    [Fact]
    public async Task OnCreateAsync_JwtAndSessionForDifferentRegistrations_ThrowsMismatch()
    {
        var (jwtRegistration, _) = await CreateRegistrationAsync();
        var (_, otherSessionToken) = await CreateRegistrationAsync();

        var jwt = _tokenService.GenerateRegistrationToken(
            jwtRegistration.Id,
            RegistrationAuthLevel.Authorized,
            jwtRegistration.ExpiresAt);

        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.SessionTokenHeader] = otherSessionToken;

        SetRequestCookie(context, RegistrationRequest.CookieName, jwt);

        var builder = OperationRequestBuilder.New();
        var act = async () => await InvokeAsync(context, builder);
        var exception = await act
            .Should()
            .ThrowAsync<GraphQLException>();

        exception.Which.Errors
            .Should()
            .ContainSingle(x => x.Code == "REGISTRATION_SESSION_MISMATCH");
    }

    [Fact]
    public async Task OnCreateAsync_SessionWithoutJwt_IssuesChallenge()
    {
        var (registration, sessionToken) = await CreateRegistrationAsync();
        var context = CreateHttpContext();

        context.Request.Headers[RegistrationRequest.SessionTokenHeader] = sessionToken;

        var builder = OperationRequestBuilder.New();

        await InvokeAsync(context, builder);

        var verification = await Context
            .RegistrationEmailVerifications
            .SingleOrDefaultAsync(x => x.RegistrationSessionId == registration.Id);

        verification.Should().NotBeNull();

        context.Response.Headers.SetCookie
            .ToString()
            .Should()
            .Contain(RegistrationRequest.CookieName);
    }

    private async Task InvokeAsync(HttpContext context, OperationRequestBuilder builder)
    {
        var executor = await Factory.Services.GetRequestExecutorAsync();

        await _sut.OnCreateAsync(context, executor, builder, CancellationToken.None);
    }

    private DefaultHttpContext CreateHttpContext() => new() { RequestServices = Scope.ServiceProvider };

    private static void SetRequestCookie(HttpContext context, string name, string value) =>
        context.Request.Headers.Cookie = $"{name}={value}";

    private async Task<(RegistrationSession Registration, string SessionToken)> CreateRegistrationAsync(DateTime? expiresAt = null)
    {
        var sessionToken = _cryptoService.GenerateSessionToken();

        var registration = new RegistrationSession
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@planara.test",
            SessionTokenHash = _cryptoService.HashSessionToken(sessionToken),
            CurrentStep = RegistrationStep.Code,
            NextStep = RegistrationStep.Password,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1)
        };

        Context.RegistrationSessions.Add(registration);
        await Context.SaveChangesAsync();

        return (registration, sessionToken);
    }
}