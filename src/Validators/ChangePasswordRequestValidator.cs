using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty()
            .WithMessage("Текущий пароль обязателен.");

        RuleFor(x => x.NewPassword)
            .ValidPassword();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("Подтверждение пароля обязательно.")
            .Equal(x => x.NewPassword)
            .WithMessage("Пароли не совпадают.");
    }
}