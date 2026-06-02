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
        var request = new RegisterRequest { Email = "akf", Password = "Qwerty1!", Consent = true };

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
        var request = new RegisterRequest { Email = "a@b.com", Password = "QWERTY1!", Consent = true }; // нет lower

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("строчную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingUpper_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "qwerty1!", Consent = true }; // нет upper

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("заглавную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingDigit_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "Qwerty!!", Consent = true };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("цифру", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Register_PasswordMissingSpecial_Fails()
    {
        var validator = new RegisterRequestValidator();
        var request = new RegisterRequest { Email = "a@b.com", Password = "Qwerty12", Consent = true };

        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("спецсимвол", StringComparison.OrdinalIgnoreCase));
    }
    
    [Fact]
    public void RegisterRequest_ValidRequest_Succeeds()
    {
        var validator = new RegisterRequestValidator();

        var result = validator.Validate(new RegisterRequest
        {
            Email = "test@planara.local",
            Password = "Password1!",
            Consent = true
        });

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("PASSWORD1!", "строчную")]
    [InlineData("password1!", "заглавную")]
    [InlineData("Password!", "цифру")]
    [InlineData("Password1", "спецсимвол")]
    public void RegisterRequest_InvalidPasswordComplexity_Fails(
        string password,
        string expectedMessagePart)
    {
        var validator = new RegisterRequestValidator();

        var result = validator.Validate(new RegisterRequest
        {
            Email = "test@planara.local",
            Password = password,
            Consent = true
        });

        result.IsValid.Should().BeFalse();

        result.Errors.Should().Contain(x =>
            x.ErrorMessage.Contains(expectedMessagePart, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegisterRequest_EmptyPassword_Fails()
    {
        var validator = new RegisterRequestValidator();

        var result = validator.Validate(new RegisterRequest
        {
            Email = "test@planara.local",
            Password = "",
            Consent = true
        });

        result.IsValid.Should().BeFalse();

        result.Errors.Should().Contain(x =>
            x.ErrorMessage.Contains("Пароль обязателен", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegisterRequest_NullPassword_Fails()
    {
        var validator = new RegisterRequestValidator();

        var result = validator.Validate(new RegisterRequest
        {
            Email = "test@planara.local",
            Password = null!,
            Consent = true
        });

        result.IsValid.Should().BeFalse();

        result.Errors.Should().Contain(x =>
            x.ErrorMessage.Contains("Пароль обязателен", StringComparison.OrdinalIgnoreCase));
    }
}