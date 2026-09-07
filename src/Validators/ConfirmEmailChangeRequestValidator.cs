using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

/// <summary>
/// Валидатор запроса на подтверждение изменения адреса электронной почты
/// </summary>
public class ConfirmEmailChangeRequestValidator : AbstractValidator<ConfirmEmailChangeRequest>
{
    public ConfirmEmailChangeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Код подтверждения обязателен.")
            .Length(4)
            .WithMessage("Код подтверждения должен содержать 4 символа.");
    }
}