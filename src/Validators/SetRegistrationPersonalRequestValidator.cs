using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class SetRegistrationPersonalRequestValidator : AbstractValidator<SetRegistrationPersonalRequest>
{
    public SetRegistrationPersonalRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100)
            .WithMessage("Имя не должно превышать 100 символов.");

        RuleFor(x => x.Surname)
            .MaximumLength(100)
            .WithMessage("Фамилия не должна превышать 100 символов.");
    }
}