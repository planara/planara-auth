using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class SetRegistrationPasswordRequestValidator : AbstractValidator<SetRegistrationPasswordRequest>
{
    public SetRegistrationPasswordRequestValidator()
    {
        RuleFor(x => x.Password)
            .ValidPassword();

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("Подтверждение пароля обязательно.")
            .Equal(x => x.Password)
            .WithMessage("Пароли не совпадают.");
    }
}