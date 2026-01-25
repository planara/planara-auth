using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

/// <summary>
/// Валидатор запроса на обновление access token
/// </summary>
public sealed class RefreshRequestValidator: AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token обязателен.")
            .MaximumLength(512).WithMessage("Refresh token слишком длинный.");
    }
}