using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Planara.Auth.Registration;
using Planara.Common.Auth.Jwt;
using Planara.Auth.Services;
using Planara.Common.Auth.Claims;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.Tests.Unit;

public class TokenServiceTests: IClassFixture<ApiTestWebAppFactory>, IDisposable
{
    private readonly IServiceScope _scope;
    private readonly ITokenService _service;
    private readonly JwtOptions _jwt;

    public TokenServiceTests(ApiTestWebAppFactory factory)
    {
        _scope = factory.Services.CreateScope();

        _service = _scope.ServiceProvider
            .GetRequiredService<ITokenService>();

        _jwt = _scope.ServiceProvider
            .GetRequiredService<IOptions<JwtOptions>>()
            .Value;
    }
    
    [Fact]
    public void GenerateAccessToken_ReturnsJwtAndExpiry()
    {
        var userId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(ClaimTypes.UserId, userId.ToString()),
            new Claim("custom", "x")
        };

        var (token, exp) = _service.GenerateAccessToken(claims);

        token.Should().NotBeNullOrWhiteSpace();
        token.Count(c => c == '.').Should().Be(2, "JWT должен состоять из 3 частей, разделённых точками");

        exp.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        exp.Should().BeBefore(DateTime.UtcNow.AddHours(2));
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsRawAndHash()
    {
        var (raw, hash) = _service.GenerateRefreshToken();

        raw.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(raw, "в БД должен храниться хэш, а не сырой токен");
    }

    [Fact]
    public void HashRefreshToken_IsDeterministic()
    {
        var (raw, hash) = _service.GenerateRefreshToken();
        var computed = _service.HashRefreshToken(raw);

        computed.Should().Be(hash);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldBeUnique()
    {
        var (raw1, _) = _service.GenerateRefreshToken();
        var (raw2, _) = _service.GenerateRefreshToken();

        raw1.Should().NotBe(raw2);
    }
    
    [Fact]
    public void GenerateRegistrationToken_ValidData_ReturnsToken()
    {
        var registrationId = Guid.NewGuid();
        var token = _service.GenerateRegistrationToken(registrationId, RegistrationAuthLevel.Challenge, DateTime.UtcNow.AddMinutes(10));

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ValidateRegistrationToken_ValidToken_ReturnsPrincipal()
    {
        var registrationId = Guid.NewGuid();
        var token = _service.GenerateRegistrationToken(registrationId, RegistrationAuthLevel.Authorized, DateTime.UtcNow.AddMinutes(10));
        var principal = _service.ValidateRegistrationToken(token);

        principal.Should().NotBeNull();
        principal.GetUserId().Should().Be(registrationId);
        principal.GetRegistrationAuthLevel()
            .Should()
            .Be(RegistrationAuthLevel.Authorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ValidateRegistrationToken_EmptyToken_ReturnsNull(string token)
    {
        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateRegistrationToken_InvalidToken_ReturnsNull()
    {
        var result = _service.ValidateRegistrationToken("invalid-token");

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateRegistrationToken_ExpiredToken_ReturnsNull()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims:
            [
                new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
                new Claim(RegistrationClaimTypes.AuthLevel, RegistrationAuthLevel.Challenge.ToString())
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-3),
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);

        var rawToken = new JwtSecurityTokenHandler().WriteToken(token);
        var result = _service.ValidateRegistrationToken(rawToken);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateAccessToken_ReplacesExistingTokenUse()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration)
        };

        var (token, _) = _service.GenerateAccessToken(claims);

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsTokenAndHash()
    {
        var (token, hash) = _service.GenerateRefreshToken();

        token.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBeNullOrWhiteSpace();

        _service.HashRefreshToken(token)
            .Should()
            .Be(hash);
    }
    
    [Fact]
    public void ValidateRegistrationToken_AccessToken_ReturnsNull()
    {
        var (token, _) = _service.GenerateAccessToken([new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString())]);
        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_MissingTokenUse_ReturnsNull()
    {
        var token = CreateRegistrationToken(
        [
            new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(RegistrationClaimTypes.AuthLevel, RegistrationAuthLevel.Challenge.ToString())
        ]);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_MissingAuthLevel_ReturnsNull()
    {
        var token = CreateRegistrationToken(
        [
            new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration)
        ]);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_InvalidAuthLevel_ReturnsNull()
    {
        var token = CreateRegistrationToken(
        [
            new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
            new Claim(RegistrationClaimTypes.AuthLevel, "invalid")
        ]);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_MissingUserId_ReturnsNull()
    {
        var token = CreateRegistrationToken(
        [
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
            new Claim(RegistrationClaimTypes.AuthLevel, RegistrationAuthLevel.Challenge.ToString())
        ]);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_InvalidUserId_ReturnsNull()
    {
        var token = CreateRegistrationToken(
        [
            new Claim(ClaimTypes.UserId, "invalid-guid"),
            new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
            new Claim(RegistrationClaimTypes.AuthLevel, RegistrationAuthLevel.Challenge.ToString())
        ]);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }
    
    [Fact]
    public void ValidateRegistrationToken_DifferentAlgorithm_ReturnsNull()
    {
        var token = CreateRegistrationToken(
            [
                new Claim(ClaimTypes.UserId, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.TokenUse, TokenUses.Registration),
                new Claim(RegistrationClaimTypes.AuthLevel, RegistrationAuthLevel.Challenge.ToString())
            ],
            SecurityAlgorithms.HmacSha384);

        var result = _service.ValidateRegistrationToken(token);

        result.Should().BeNull();
    }

    public void Dispose() => _scope.Dispose();
    
    private string CreateRegistrationToken(IEnumerable<Claim> claims, string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var credentials = new SigningCredentials(key, algorithm);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}