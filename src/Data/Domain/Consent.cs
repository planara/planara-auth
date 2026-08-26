using System.ComponentModel.DataAnnotations;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Согласие пользователя
/// </summary>
public class Consent : BaseEntity
{
    // Версия согласия
    public Guid ConsentVersionId { get; set; }
    public ConsentVersion ConsentVersion { get; set; } = null!;

    /// <summary>
    /// Время предоставления согласия
    /// </summary>
    public DateTime GivenAt { get; set; }

    /// <summary>
    /// Время отзыва согласия
    /// </summary>
    public DateTime? RevokedAt { get; set; }
    
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(1024)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// Незавершённая регистрация
    /// </summary>
    public Guid? RegistrationSessionId { get; set; }
    public RegistrationSession? RegistrationSession { get; set; }

    /// <summary>
    /// Учётные данные зарегистрированного пользователя
    /// </summary>
    public Guid? UserCredentialId { get; set; }
    public UserCredential? UserCredential { get; set; }
}