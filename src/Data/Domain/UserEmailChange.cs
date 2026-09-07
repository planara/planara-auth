using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Запрос на изменение адреса электронной почты пользователя
/// </summary>
public class UserEmailChange : BaseEntity
{
    /// <summary>
    /// Идентификатор пользователя
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Новый адрес электронной почты
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Хэш кода подтверждения
    /// </summary>
    public required string CodeHash { get; set; }

    /// <summary>
    /// Количество неудачных попыток ввода кода
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Время истечения срока действия кода
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Время, начиная с которого разрешена повторная отправка кода
    /// </summary>
    public DateTime ResendAvailableAt { get; set; }
}