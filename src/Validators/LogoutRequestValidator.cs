using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

/// <summary>
/// Валидатор запроса на выход из аккаунта
/// </summary>
public sealed class LogoutRequestValidator: AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token обязателен.")
            .MaximumLength(512).WithMessage("Refresh token слишком длинный.");
    }
}