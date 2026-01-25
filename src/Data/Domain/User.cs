using System.ComponentModel.DataAnnotations;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Данные для входа/регистрации пользователя
/// </summary>
public class UserCredential: BaseEntity
{
    /// <summary>
    /// ID пользователя
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Почта
    /// </summary>
    [EmailAddress]
    [MaxLength(256)]
    public required string Email { get; set; }
    
    /// <summary>
    /// Пароль
    /// </summary>
    [MaxLength(100)]
    public required string PasswordHash { get; set; }
}