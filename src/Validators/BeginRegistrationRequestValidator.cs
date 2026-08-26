using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class BeginRegistrationRequestValidator: AbstractValidator<BeginRegistrationRequest>
{
    public  BeginRegistrationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email обязателен.")
            .EmailAddress().WithMessage("Email должен быть валидным адресом.")
            .MaximumLength(256).WithMessage("Email не должен превышать 256 символов.");
        
        RuleFor(x => x.PersonalDataConsent)
            .Equal(true)
            .WithMessage("Необходимо дать согласие на обработку персональных данных.");
    }
}