using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email обязателен.")
            .EmailAddress().WithMessage("Email должен быть валидным адресом.")
            .MaximumLength(256).WithMessage("Email не должен превышать 256 символов.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Пароль обязателен.")
            .MinimumLength(8).WithMessage("Пароль должен быть не короче 8 символов.")
            .MaximumLength(72).WithMessage("Пароль должен быть не длиннее 72 символов.")
            .Must(ContainsLower).WithMessage("Пароль должен содержать хотя бы одну строчную букву.")
            .Must(ContainsUpper).WithMessage("Пароль должен содержать хотя бы одну заглавную букву.")
            .Must(ContainsDigit).WithMessage("Пароль должен содержать хотя бы одну цифру.")
            .Must(ContainsSpecial).WithMessage("Пароль должен содержать хотя бы один спецсимвол.");
    }

    private static bool ContainsLower(string? s) => !string.IsNullOrEmpty(s) && s.Any(char.IsLower);
    private static bool ContainsUpper(string? s) => !string.IsNullOrEmpty(s) && s.Any(char.IsUpper);
    private static bool ContainsDigit(string? s) => !string.IsNullOrEmpty(s) && s.Any(char.IsDigit);
    private static bool ContainsSpecial(string? s) => !string.IsNullOrEmpty(s) && s.Any(ch => !char.IsLetterOrDigit(ch));
}