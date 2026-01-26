using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Planara.Auth.Options;
using Planara.Auth.Services;
using ClaimTypes = Planara.Common.Auth.Claims.ClaimTypes;

namespace Planara.Auth.Tests.Unit;

public class TokenServiceTests
{
    private readonly ITokenService _sut;
    
    public TokenServiceTests()
    {
        var opts = new OptionsWrapper<JwtOptions>(new JwtOptions
        {
            Issuer = "planara-auth",
            Audience = "planara",
            SigningKey = "9b6d3b3c5a0d4f1f9a7e1c2d3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a",
            AccessTokenMinutes = 15
        });

        _sut = new TokenService(opts);
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

        var (token, exp) = _sut.GenerateAccessToken(claims);

        token.Should().NotBeNullOrWhiteSpace();
        token.Count(c => c == '.').Should().Be(2, "JWT должен состоять из 3 частей, разделённых точками");

        exp.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        exp.Should().BeBefore(DateTime.UtcNow.AddHours(2));
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsRawAndHash()
    {
        var (raw, hash) = _sut.GenerateRefreshToken();

        raw.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(raw, "в БД должен храниться хэш, а не сырой токен");
    }

    [Fact]
    public void HashRefreshToken_IsDeterministic()
    {
        var (raw, hash) = _sut.GenerateRefreshToken();

        var computed = _sut.HashRefreshToken(raw);

        computed.Should().Be(hash);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldBeUnique()
    {
        var (raw1, _) = _sut.GenerateRefreshToken();
        var (raw2, _) = _sut.GenerateRefreshToken();

        raw1.Should().NotBe(raw2);
    }
}