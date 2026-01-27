using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Common.Kafka;
using Planara.Kafka.Configurations;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<UserCreatedMessage> producer,
    ILogger<OutboxPublisher> logger): BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private const int BATCH_SIZE = 50;
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PublishOnce(cancellationToken);
            }
            catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Cancellation requested, outbox publisher stopped");
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Outbox publisher crashed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
    
    private async Task PublishOnce(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lockFor = TimeSpan.FromSeconds(30);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

        List<OutboxMessage> batch;

        // Получение сообщений на отправку
        await using (var tx = await dataContext.Database.BeginTransactionAsync(ct))
        {
            batch = await dataContext.OutboxMessages
                .FromSqlRaw(@"
                    SELECT *
                    FROM ""OutboxMessages""
                    WHERE ""ProcessedAt"" IS NULL
                      AND (""LockedUntil"" IS NULL OR ""LockedUntil"" < {0})
                      AND ""Type"" = {1}
                    ORDER BY ""CreatedAt"", ""Id""
                    FOR UPDATE SKIP LOCKED
                    LIMIT {2};
                ", now, nameof(UserCreatedMessage), BATCH_SIZE)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                await tx.CommitAsync(ct);
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
                return;
            }

            foreach (var m in batch)
            {
                m.LockedUntil = now.Add(lockFor);
                m.LockedBy = _workerId;
                m.UpdatedAt = now;
            }

            await dataContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Публикация сообщений в Kafka
        foreach (var m in batch)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<UserCreatedMessage>(m.PayloadJson, KafkaJson.DeserializerOptions)
                          ?? throw new InvalidOperationException("PayloadJson deserialized to null");

                await producer.ProduceAsync(m.TopicKey, m.Key, msg, ct);

                var doneAt = DateTime.UtcNow;
                m.ProcessedAt = doneAt;
                m.UpdatedAt = doneAt;
                m.LastError = null;
            }
            catch (Exception e)
            {
                var failAt = DateTime.UtcNow;

                m.AttemptCount += 1;
                m.LastAttemptAt = failAt;
                m.LastError = e.ToString().Length > 4000 ? e.ToString()[..4000] : e.ToString();

                var delaySeconds = Math.Min(60, 2 * m.AttemptCount);
                m.LockedUntil = failAt.AddSeconds(delaySeconds);
                m.UpdatedAt = failAt;

                logger.LogWarning(e, "Failed to publish outbox message {Id}", m.Id);
            }
        }

        await dataContext.SaveChangesAsync(ct);
    }
}