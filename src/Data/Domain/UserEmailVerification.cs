using System.ComponentModel.DataAnnotations;
using Planara.Auth.Data.Enums;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Подтверждение адреса электронной почты пользователя
/// </summary>
public class UserEmailVerification : BaseEntity
{
    /// <summary>
    /// Идентификатор пользователя
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Учетные данные пользователя
    /// </summary>
    public UserCredential UserCredential { get; set; } = null!;

    /// <summary>
    /// Назначение подтверждения
    /// </summary>
    public EmailVerificationType Type { get; set; }

    /// <summary>
    /// Адрес электронной почты, который подтверждается
    /// </summary>
    [MaxLength(256)]
    public required string Email { get; set; }

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