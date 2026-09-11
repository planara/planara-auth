namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на изменение пароля пользователя
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>
    /// Текущий пароль
    /// </summary>
    public required string CurrentPassword { get; init; }
    
    /// <summary>
    /// Подтвержденный текущий пароль
    /// </summary>
    public required string ConfirmPassword { get; init; }

    /// <summary>
    /// Новый пароль
    /// </summary>
    public required string NewPassword { get; init; }
}