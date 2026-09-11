using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Planara.Auth.Data.Enums;
using Planara.Auth.Registration;
using SameSiteMode =  Microsoft.Net.Http.Headers.SameSiteMode;

namespace Planara.Auth.Tests.Unit;

public class RegistrationRequestTests
{
    [Fact]
    public void SetCookie_AppendsRegistrationCookie()
    {
        var context = new DefaultHttpContext();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        RegistrationRequest.SetCookie(context, "registration-jwt", expiresAt);

        var cookie = GetCookie(context, RegistrationRequest.CookieName);

        cookie.Value.ToString().Should().Be("registration-jwt");
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookie.Path.ToString().Should().Be("/");
        cookie.Expires.Should().NotBeNull();
        cookie.Expires!.Value.UtcDateTime
            .Should()
            .BeCloseTo(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SetRegistrationCookie_AppendsFlowCookie()
    {
        var context = new DefaultHttpContext();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        RegistrationRequest.SetRegistrationCookie(context, expiresAt);

        var cookie = GetCookie(context, RegistrationRequest.FlowHeader);

        cookie.Value.ToString().Should().Be("1");
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookie.Path.ToString().Should().Be("/");
        cookie.Expires.Should().NotBeNull();
        cookie.Expires!.Value.UtcDateTime
            .Should()
            .BeCloseTo(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DeleteCookie_ExpiresRegistrationCookie()
    {
        var context = new DefaultHttpContext();

        RegistrationRequest.DeleteCookie(context);

        var cookie = GetCookie(context, RegistrationRequest.CookieName);

        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookie.Path.ToString().Should().Be("/");
        cookie.Expires.Should().NotBeNull();
        cookie.Expires!.Value.Should().BeBefore(DateTimeOffset.UtcNow);
    }
    
    [Fact]
    public void HasSession_AllValuesPresent_ReturnsTrue()
    {
        _valid.HasSession.Should().BeTrue();
        _valid.RegistrationId.Should().NotBeNull();
        _valid.Email.Should().Be("test@planara.ru");
        _valid.SessionToken.Should().Be("session-token");
        _valid.CurrentStep.Should().Be(RegistrationStep.Code);
        _valid.RequiredStep.Should().Be(RegistrationStep.Code);
        _valid.AuthLevel.Should().Be(RegistrationAuthLevel.Challenge);
    }

    [Fact]
    public void HasSession_MissingRegistrationId_ReturnsFalse()
    {
        var context = _valid with { RegistrationId = null };

        context.HasSession.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void HasSession_InvalidSessionToken_ReturnsFalse(string? sessionToken)
    {
        var context = _valid with { SessionToken = sessionToken };

        context.HasSession.Should().BeFalse();
    }

    [Fact]
    public void HasSession_MissingCurrentStep_ReturnsFalse()
    {
        var context = _valid with { CurrentStep = null };

        context.HasSession.Should().BeFalse();
    }

    [Fact]
    public void HasSession_MissingRequiredStep_ReturnsFalse()
    {
        var context = _valid with { RequiredStep = null };

        context.HasSession.Should().BeFalse();
    }

    [Fact]
    public void HasSession_MissingAuthLevel_ReturnsFalse()
    {
        var context = _valid with { AuthLevel = null };

        context.HasSession.Should().BeFalse();
    }

    [Fact]
    public void Empty_HasNoSession()
    {
        var context = RegistrationRequestContext.Empty;

        context.RegistrationId.Should().BeNull();
        context.Email.Should().BeNull();
        context.SessionToken.Should().BeNull();
        context.CurrentStep.Should().BeNull();
        context.RequiredStep.Should().BeNull();
        context.AuthLevel.Should().BeNull();
        context.HasSession.Should().BeFalse();
    }

    private static SetCookieHeaderValue GetCookie(HttpContext context, string name)
    {
        var values = context.Response.Headers.SetCookie
            .Select(x => x!)
            .ToList();

        var cookies = SetCookieHeaderValue.ParseList(values);

        return cookies.Single(x => x.Name.ToString() == name);
    }
    
    private readonly RegistrationRequestContext _valid = new(
        RegistrationId: Guid.NewGuid(),
        Email: "test@planara.ru",
        SessionToken: "session-token",
        CurrentStep: RegistrationStep.Code,
        RequiredStep: RegistrationStep.Code,
        AuthLevel: RegistrationAuthLevel.Challenge);
}