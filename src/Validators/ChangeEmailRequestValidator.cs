using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

/// <summary>
/// Валидатор запроса на изменение адреса электронной почты
/// </summary>
public sealed class ChangeEmailRequestValidator : AbstractValidator<ChangeEmailRequest>
{
    public ChangeEmailRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Email обязателен.")
            .EmailAddress()
            .WithMessage("Email должен быть валидным адресом.")
            .MaximumLength(256)
            .WithMessage("Email не должен превышать 256 символов.");
    }
}