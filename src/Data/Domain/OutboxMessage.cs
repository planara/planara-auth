using System.ComponentModel.DataAnnotations;
using Planara.Common.Database.Domain;

namespace Planara.Auth.Data.Domain;

/// <summary>
/// Outbox-сообщение для надежной публикации событий
/// </summary>
public class OutboxMessage: BaseEntity
{
    /// <summary>
    /// Ключ топика (логическое имя)
    /// </summary>
    [MaxLength(200)]
    public string TopicKey { get; set; } = null!;
    
    /// <summary>
    /// Тип сообщения/события
    /// </summary>
    [MaxLength(200)]
    public string Type { get; set; } = null!;
    
    /// <summary>
    /// Ключ Kafka-сообщения (partition key)
    /// </summary>
    [MaxLength(200)]
    public string Key { get; set; } = null!;

    /// <summary>
    /// JSON-представление полезной нагрузки сообщения
    /// </summary>
    public string PayloadJson { get; set; } = null!;

    /// <summary>
    /// Время успешной обработки (публикации) сообщения во внешний брокер
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Время, до которого сообщение "залочено" конкретным воркером на обработку
    /// </summary>
    public DateTime? LockedUntil { get; set; }
    
    /// <summary>
    /// Идентификатор воркера сервиса, который взял сообщение в обработку
    /// </summary>
    [MaxLength(200)]
    public string? LockedBy { get; set; }

    /// <summary>
    /// Количество попыток публикации сообщения во внешний брокер
    /// </summary>
    public int AttemptCount { get; set; }
    
    /// <summary>
    /// Время последней попытки публикации сообщения
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }
    
    /// <summary>
    /// Текст последней ошибки публикации (если была)
    /// </summary>
    [MaxLength(4000)]
    public string? LastError { get; set; }
}