using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Refresh-токен пользователя
/// </summary>
public class RefreshToken: BaseEntity
{
    /// <summary>
    /// ID пользователя
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Хэш исходного refresh-токена
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Дата и время окончания срока действия refresh-токена (UTC)
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Дата и время выпуска refresh-токена (UTC)
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
    
    /// <summary>
    /// Дата и время отзыва refresh-токена (UTC)
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }
    
    /// <summary>
    /// Хэш нового refresh-токена, которым был заменён текущий
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }
    
    /// <summary>
    /// IP-адрес клиента, с которого был выпущен refresh-токен
    /// </summary>
    public string? CreatedByIp { get; set; }
    
    /// <summary>
    /// User-Agent клиента, с которого был выпущен refresh-токен
    /// </summary>
    public string? UserAgent { get; set; }
}
