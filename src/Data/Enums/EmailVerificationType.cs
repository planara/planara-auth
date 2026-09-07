namespace Planara.Auth.Data.Enums;

/// <summary>
/// Назначение подтверждения адреса электронной почты
/// </summary>
public enum EmailVerificationType
{
    /// <summary>
    /// Изменение адреса электронной почты
    /// </summary>
    EmailChange = 1,

    /// <summary>
    /// Подтверждение текущего адреса электронной почты
    /// </summary>
    EmailConfirmation = 2
}