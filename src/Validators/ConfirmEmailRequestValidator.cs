using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public sealed class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Код подтверждения обязателен.")
            .Length(4)
            .WithMessage("Код подтверждения должен содержать 4 символа.");
    }
}