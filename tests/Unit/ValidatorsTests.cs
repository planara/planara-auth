using FluentAssertions;
using Planara.Auth.Requests;
using Planara.Auth.Validators;

namespace Planara.Auth.Tests.Unit;

public class ValidatorsTests
{
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
    public void BeginRegistration_InvalidEmail_Fails()
    {
        var validator = new BeginRegistrationRequestValidator();
        var request = new BeginRegistrationRequest
        {
            Email = "invalid",
            PersonalDataConsent = true,
            ConsentVersionId = Guid.NewGuid()
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BeginRegistration_EmptyConsentVersionId_Fails()
    {
        var validator = new BeginRegistrationRequestValidator();
        var request = new BeginRegistrationRequest
        {
            Email = "test@planara.ru",
            PersonalDataConsent = true,
            ConsentVersionId = Guid.Empty
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void BeginRegistration_ValidRequest_Passes()
    {
        var validator = new BeginRegistrationRequestValidator();
        var request = new BeginRegistrationRequest
        {
            Email = "test@planara.ru",
            PersonalDataConsent = true,
            ConsentVersionId = Guid.NewGuid()
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyRegistrationCode_EmptyCode_Fails()
    {
        var validator = new VerifyRegistrationCodeRequestValidator();
        var request = new VerifyRegistrationCodeRequest { Code = "" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyRegistrationCode_InvalidLength_Fails()
    {
        var validator = new VerifyRegistrationCodeRequestValidator();
        var request = new VerifyRegistrationCodeRequest { Code = "123" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyRegistrationCode_ValidCode_Passes()
    {
        var validator = new VerifyRegistrationCodeRequestValidator();
        var request = new VerifyRegistrationCodeRequest { Code = "1234" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SetRegistrationPassword_MissingLower_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "PASSWORD1!",
            ConfirmPassword = "PASSWORD1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("строчную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetRegistrationPassword_MissingUpper_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "password1!",
            ConfirmPassword = "password1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("заглавную", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetRegistrationPassword_MissingDigit_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "Password!",
            ConfirmPassword = "Password!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("цифру", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetRegistrationPassword_MissingSpecial_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "Password1",
            ConfirmPassword = "Password1"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
        res.Errors.Should().Contain(e => e.ErrorMessage.Contains("спецсимвол", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetRegistrationPassword_TooShort_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "Pas1!",
            ConfirmPassword = "Pas1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetRegistrationPassword_TooLong_Fails()
    {
        var password = $"Password1!{new string('a', 63)}";

        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = password,
            ConfirmPassword = password
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetRegistrationPassword_ConfirmPasswordMismatch_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "Password1!",
            ConfirmPassword = "Password2!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetRegistrationPassword_ValidRequest_Passes()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = "Password1!",
            ConfirmPassword = "Password1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SetRegistrationPersonal_NameTooLong_Fails()
    {
        var validator = new SetRegistrationPersonalRequestValidator();
        var request = new SetRegistrationPersonalRequest
        {
            Name = new string('a', 101),
            Surname = "Surname"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetRegistrationPersonal_SurnameTooLong_Fails()
    {
        var validator = new SetRegistrationPersonalRequestValidator();
        var request = new SetRegistrationPersonalRequest
        {
            Name = "Name",
            Surname = new string('a', 101)
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetRegistrationPersonal_ValidRequest_Passes()
    {
        var validator = new SetRegistrationPersonalRequestValidator();
        var request = new SetRegistrationPersonalRequest
        {
            Name = "Name",
            Surname = "Surname"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ChangePassword_EmptyCurrentPassword_Fails()
    {
        var validator = new ChangePasswordRequestValidator();
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "",
            NewPassword = "NewPassword1!",
            ConfirmPassword = "NewPassword1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ChangePassword_InvalidNewPassword_Fails()
    {
        var validator = new ChangePasswordRequestValidator();
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "CurrentPassword1!",
            NewPassword = "password",
            ConfirmPassword = "password"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ChangePassword_ConfirmPasswordMismatch_Fails()
    {
        var validator = new ChangePasswordRequestValidator();
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "CurrentPassword1!",
            NewPassword = "NewPassword1!",
            ConfirmPassword = "OtherPassword1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ChangePassword_ValidRequest_Passes()
    {
        var validator = new ChangePasswordRequestValidator();
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "CurrentPassword1!",
            NewPassword = "NewPassword1!",
            ConfirmPassword = "NewPassword1!"
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ChangeEmail_InvalidEmail_Fails()
    {
        var validator = new ChangeEmailRequestValidator();
        var request = new ChangeEmailRequest { Email = "invalid" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ChangeEmail_ValidEmail_Passes()
    {
        var validator = new ChangeEmailRequestValidator();
        var request = new ChangeEmailRequest { Email = "new@planara.ru" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ChangeRegistrationEmail_InvalidEmail_Fails()
    {
        var validator = new ChangeRegistrationEmailRequestValidator();
        var request = new ChangeRegistrationEmailRequest { Email = "invalid" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ChangeRegistrationEmail_ValidEmail_Passes()
    {
        var validator = new ChangeRegistrationEmailRequestValidator();
        var request = new ChangeRegistrationEmailRequest { Email = "new@planara.ru" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ConfirmEmailChange_InvalidCodeLength_Fails()
    {
        var validator = new ConfirmEmailChangeRequestValidator();
        var request = new ConfirmEmailChangeRequest { Code = "123" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ConfirmEmailChange_ValidCode_Passes()
    {
        var validator = new ConfirmEmailChangeRequestValidator();
        var request = new ConfirmEmailChangeRequest { Code = "1234" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ConfirmEmail_InvalidCodeLength_Fails()
    {
        var validator = new ConfirmEmailRequestValidator();
        var request = new ConfirmEmailRequest { Code = "12345" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ConfirmEmail_ValidCode_Passes()
    {
        var validator = new ConfirmEmailRequestValidator();
        var request = new ConfirmEmailRequest { Code = "1234" };
        var res = validator.Validate(request);

        res.IsValid.Should().BeTrue();
    }
    
    [Fact]
    public void SetRegistrationPassword_NullPassword_Fails()
    {
        var validator = new SetRegistrationPasswordRequestValidator();
        var request = new SetRegistrationPasswordRequest
        {
            Password = null!,
            ConfirmPassword = null!
        };
        var res = validator.Validate(request);

        res.IsValid.Should().BeFalse();
    }
}