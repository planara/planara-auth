using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Planara.Auth.Services;

namespace Planara.Auth.Tests.Unit;

public class RegistrationCryptoServiceTests : IClassFixture<ApiTestWebAppFactory>, IDisposable
{
    private readonly IServiceScope _scope;
    private readonly IRegistrationCryptoService _service;

    public RegistrationCryptoServiceTests(ApiTestWebAppFactory factory)
    {
        _scope = factory.Services.CreateScope();
        _service = _scope.ServiceProvider
            .GetRequiredService<IRegistrationCryptoService>();
    }

    [Fact]
    public void Constructor_MissingSecret_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var action = () => new RegistrationCryptoService(configuration);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenerateCode_ReturnsFourDigitCode()
    {
        var code = _service.GenerateCode();

        code.Should().HaveLength(4);
        code.Should().MatchRegex(@"^\d{4}$");
    }

    [Fact]
    public void GenerateCode_ReturnsDifferentValues()
    {
        var codes = Enumerable.Range(0, 20)
            .Select(_ => _service.GenerateCode())
            .ToHashSet();

        codes.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void GenerateSessionToken_ReturnsNonEmptyToken()
    {
        var token = _service.GenerateSessionToken();

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateSessionToken_ReturnsDifferentValues()
    {
        var first = _service.GenerateSessionToken();
        var second = _service.GenerateSessionToken();

        second.Should().NotBe(first);
    }

    [Fact]
    public void HashCode_SameCode_ReturnsSameHash()
    {
        var first = _service.HashCode("1234");
        var second = _service.HashCode("1234");

        second.Should().Be(first);
    }

    [Fact]
    public void HashCode_DifferentCode_ReturnsDifferentHash()
    {
        var first = _service.HashCode("1234");
        var second = _service.HashCode("5678");

        second.Should().NotBe(first);
    }

    [Fact]
    public void VerifyCode_CorrectCode_ReturnsTrue()
    {
        var hash = _service.HashCode("1234");
        var result = _service.VerifyCode("1234", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_WrongCode_ReturnsFalse()
    {
        var hash = _service.HashCode("1234");
        var result = _service.VerifyCode("5678", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void HashSessionToken_SameToken_ReturnsSameHash()
    {
        var first = _service.HashSessionToken("session-token");
        var second = _service.HashSessionToken("session-token");

        second.Should().Be(first);
    }

    [Fact]
    public void HashSessionToken_DifferentToken_ReturnsDifferentHash()
    {
        var first = _service.HashSessionToken("first-token");
        var second = _service.HashSessionToken("second-token");

        second.Should().NotBe(first);
    }

    [Fact]
    public void VerifySessionToken_CorrectToken_ReturnsTrue()
    {
        var hash = _service.HashSessionToken("session-token");
        var result = _service.VerifySessionToken("session-token", hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifySessionToken_WrongToken_ReturnsFalse()
    {
        var hash = _service.HashSessionToken("session-token");
        var result = _service.VerifySessionToken("wrong-token", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyCode_InvalidHash_ReturnsFalse()
    {
        var result = _service.VerifyCode("1234", "invalid-hash");

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifySessionToken_InvalidHash_ReturnsFalse()
    {
        var result = _service.VerifySessionToken("session-token", "invalid-hash");

        result.Should().BeFalse();
    }

    public void Dispose() => _scope.Dispose();
}