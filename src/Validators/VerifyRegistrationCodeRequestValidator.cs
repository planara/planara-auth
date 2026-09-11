using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class VerifyRegistrationCodeRequestValidator : AbstractValidator<VerifyRegistrationCodeRequest>
{
    public VerifyRegistrationCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\d{4}$")
            .WithMessage("Код должен состоять из 4 цифр.");
    }
}