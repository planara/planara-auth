using FluentAssertions;
using Planara.Auth.Requests;
using Planara.Auth.Validators;

namespace Planara.Auth.Tests.Unit;

public class ValidatorsTests
{
    [Fact]
    public void Register_InvalidEmail_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "akf", Password = "Qwerty1!" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Login_InvalidEmail_Fails()
    {
        var validator = new LoginRequestValidator();
        var request = new LoginRequest { Email = "akf", Password = "Qwerty1!" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }
    
    [Fact]
    public void Logout_EmptyRefreshToken_Fails()
    {
        var validator = new LogoutRequestValidator();
        var request = new LogoutRequest { RefreshToken = "" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Logout_TooLongRefreshToken_Fails()
    {
        var validator = new LogoutRequestValidator();
        var request = new LogoutRequest { RefreshToken = new string('a', 513) };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Refresh_EmptyRefreshToken_Fails()
    {
        var validator = new RefreshRequestValidator();
        var request = new RefreshRequest { RefreshToken = "" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Refresh_TooLongRefreshToken_Fails()
    {
        var validator = new RefreshRequestValidator();
        var request = new RefreshRequest { RefreshToken = new string('a', 513) };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Register_PasswordMissingLower_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "QWERTY1!" }; // нет lower

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("строчную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingUpper_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "qwerty1!" }; // нет upper

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("заглавную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingDigit_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "Qwerty!!" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("цифру", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingSpecial_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "Qwerty12" };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("спецсимвол", StringComparison.OrdinalIgnoreCase));
    }
}