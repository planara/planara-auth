using System.ComponentModel.DataAnnotations;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Код подтверждения электронной почты при регистрации
/// </summary>
public class RegistrationEmailVerification : BaseEntity
{
    /// <summary>
    /// Идентификатор сессии регистрации
    /// </summary>
    public Guid RegistrationSessionId { get; set; }
    public RegistrationSession RegistrationSession { get; set; } = null!;
    
    /// <summary>
    /// Хэш кода подтверждения
    /// </summary>
    [MaxLength(256)]
    public required string CodeHash { get; set; }

    /// <summary>
    /// Количество неудачных попыток ввода
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Время окончания действия кода
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Время, после которого код можно отправить повторно
    /// </summary>
    public DateTime ResendAvailableAt { get; set; }
}