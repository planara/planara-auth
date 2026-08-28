using FluentValidation;
using Planara.Auth.Requests;

namespace Planara.Auth.Validators;

public class SetRegistrationPasswordRequestValidator : AbstractValidator<SetRegistrationPasswordRequest>
{
    public SetRegistrationPasswordRequestValidator()
    {
        RuleFor(x => x.Password)
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

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .WithMessage("Подтверждение пароля обязательно.")
            .Equal(x => x.Password)
            .WithMessage("Пароли не совпадают.");
    }

    /// <summary>
    /// Проверяет наличие хотя бы одной строчной буквы в пароле
    /// </summary>
    /// <param name="value">Пароль</param>
    private static bool ContainsLower(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsLower);

    /// <summary>
    /// Проверяет наличие хотя бы одной заглавной буквы в пароле
    /// </summary>
    /// <param name="value">Пароль</param>
    private static bool ContainsUpper(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsUpper);

    /// <summary>
    /// Проверяет наличие хотя бы одной цифры в пароле
    /// </summary>
    /// <param name="value">Пароль</param>
    private static bool ContainsDigit(string? value) => !string.IsNullOrEmpty(value) && value.Any(char.IsDigit);

    /// <summary>
    /// Проверяет наличие хотя бы одного специального символа в пароле
    /// </summary>
    /// <param name="value">Пароль</param>
    private static bool ContainsSpecial(string? value) => !string.IsNullOrEmpty(value) && value.Any(ch => !char.IsLetterOrDigit(ch));
}