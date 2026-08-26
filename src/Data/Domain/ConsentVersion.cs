using System.ComponentModel.DataAnnotations;
using Planara.Auth.Data.Enums;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

public class ConsentVersion : BaseEntity
{
    /// <summary>
    /// Тип согласия
    /// </summary>
    public ConsentType Type { get; set; }

    /// <summary>
    /// Версия документа
    /// </summary>
    [MaxLength(50)]
    public required string Version { get; set; }

    /// <summary>
    /// Заголовок документа
    /// </summary>
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>
    /// Текст согласия
    /// </summary>
    public required string Content { get; set; }
    
    /// <summary>
    /// Текст согласия (html)
    /// </summary>
    public required string HtmlContent { get; set; }

    /// <summary>
    /// Дата начала действия версии
    /// </summary>
    public DateTime EffectiveAt { get; set; }

    /// <summary>
    /// Используется ли версия для новых согласий
    /// </summary>
    public bool IsCurrent { get; set; }

    public ICollection<Consent> Consents { get; set; } = new List<Consent>();
}