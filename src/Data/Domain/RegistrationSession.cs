using System.ComponentModel.DataAnnotations;
using Planara.Auth.Data.Enums;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Прогресс регистрации
/// </summary>
public class RegistrationSession: BaseEntity
{
    /// <summary>
    /// Текущий шаг регистрации
    /// </summary>
    public RegistrationStep CurrentStep { get; set; }
    
    /// <summary>
    /// Следующий шаг регистрации
    /// </summary>
    public RegistrationStep NextStep { get; set; }
    
    /// <summary>
    /// Адрес электронной почты
    /// </summary>
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; set; }
    
    /// <summary>
    /// Подтверждена ли почта
    /// </summary>
    public bool IsEmailConfirmed { get; set; }
    
    /// <summary>
    /// Хэш пароля
    /// </summary>
    [MaxLength(100)]
    public string? PasswordHash { get; set; }
    
    /// <summary>
    /// Юзернейм
    /// </summary>
    [MaxLength(100)]
    public string? UserName { get; set; }
    
    /// <summary>
    /// Имя
    /// </summary>
    [MaxLength(100)]
    public string? Name { get; set; }
    
    /// <summary>
    /// Фамилия
    /// </summary>
    [MaxLength(100)]
    public string? Surname { get; set; }
    
    /// <summary>
    /// Ссылка на аватарку
    /// </summary>
    [MaxLength(256)]
    public string? AvatarUrl { get; set; }
    
    /// <summary>
    /// Во сколько попытка регистрации будет удалена
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// Хэш токен сессии
    /// </summary>
    [MaxLength(256)]
    public string SessionTokenHash { get; set; } = null!;
    
    public RegistrationEmailVerification? EmailVerification { get; set; }
}