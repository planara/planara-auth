using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email обязателен.")
            .EmailAddress().WithMessage("Email должен быть валидным адресом.")
            .MaximumLength(256).WithMessage("Email не должен превышать 256 символов.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Пароль обязателен.")
            .MaximumLength(72).WithMessage("Пароль должен быть не длиннее 72 символов."); // bcrypt
    }
}