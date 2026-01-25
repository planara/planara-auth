using System.ComponentModel.DataAnnotations;

namespace Planara.Auth.Requests;

/// <summary>
/// Запрос на регистрацию
/// </summary>
public sealed class RegisterRequest
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