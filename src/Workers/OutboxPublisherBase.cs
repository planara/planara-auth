using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;
using Planara.Auth.Data.Domain;
using Planara.Kafka.Configurations;
using Planara.Kafka.Interfaces;

namespace Planara.Auth.Workers;

public abstract class OutboxPublisherBase<TMessage>(
    IServiceScopeFactory scopeFactory,
    IKafkaProducer<TMessage> producer,
    ILogger logger
) : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private const int BATCH_SIZE = 50;

    [ExcludeFromCodeCoverage]
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PublishOnce(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Outbox publisher crashed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public async Task PublishOnce(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lockFor = TimeSpan.FromSeconds(30);
        var type = typeof(TMessage).Name;

        await using var scope = scopeFactory.CreateAsyncScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

        List<OutboxMessage> batch;

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
                ", now, type, BATCH_SIZE)
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

        foreach (var m in batch)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<TMessage>(
                    m.PayloadJson,
                    KafkaJson.DeserializerOptions
                ) ?? throw new InvalidOperationException("PayloadJson deserialized to null");

                await producer.ProduceAsync(m.TopicKey, m.Key, msg, ct);

                var doneAt = DateTime.UtcNow;
                m.ProcessedAt = doneAt;
                m.UpdatedAt = doneAt;
                m.LastError = null;
                m.LockedUntil = null;
            }
            catch (Exception e)
            {
                var failAt = DateTime.UtcNow;

                m.AttemptCount += 1;
                m.LastAttemptAt = failAt;
                m.LastError = e.ToString().Length > 4000
                    ? e.ToString()[..4000]
                    : e.ToString();

                var delaySeconds = Math.Min(60, 2 * m.AttemptCount);
                m.LockedUntil = failAt.AddSeconds(delaySeconds);
                m.UpdatedAt = failAt;

                logger.LogWarning(e, "Failed to publish outbox message {Id}", m.Id);
            }
        }

        await dataContext.SaveChangesAsync(ct);
    }
}