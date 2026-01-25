using System.ComponentModel.DataAnnotations;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на вход в аккаунт
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Адрес электронной почты
    /// </summary>
    [EmailAddress]
    public required string Email { get; set; }
    
    /// <summary>
    /// Пароль
    /// </summary>
    public required string Password { get; set; }
}