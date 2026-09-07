using FluentValidation;

namespace Planara.Auth.Validators;

/// <summary>
/// Расширения для валидации паролей
/// </summary>
public static class PasswordValidationExtensions
{
    /// <summary>
    /// Добавляет стандартные требования к паролю
    /// </summary>
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty()
            .WithMessage("Пароль обязателен.")
            .MinimumLength(8)
            .WithMessage("Пароль должен быть не короче 8 символов.")
            .MaximumLength(72)
            .WithMessage("Пароль должен быть не длиннее 72 символов.")
            .Must(ContainsLower)
            .WithMessage("Пароль должен содержать хотя бы одну строчную букву.")
            .Must(ContainsUpper)
            .WithMessage("Пароль должен содержать хотя бы одну заглавную букву.")
            .Must(ContainsDigit)
            .WithMessage("Пароль должен содержать хотя бы одну цифру.")
            .Must(ContainsSpecial)
            .WithMessage("Пароль должен содержать хотя бы один спецсимвол.");
    }

    private static bool ContainsLower(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsLower);

    private static bool ContainsUpper(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsUpper);

    private static bool ContainsDigit(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsDigit);

    private static bool ContainsSpecial(string? value) => !string.IsNullOrEmpty(value) && value.Any(ch => !char.IsLetterOrDigit(ch));
}