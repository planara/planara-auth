using Microsoft.EntityFrameworkCore;
using Planara.Common.Database.Domain;
using Planara.Common.Workers;

namespace Planara.Auth.Workers;

public sealed class OutboxCleanupWorker(ILogger<OutboxCleanupWorker> logger, IServiceScopeFactory scopeFactory) : CleanupWorkerBase(logger, scopeFactory)
{
    protected override string WorkerName => nameof(OutboxCleanupWorker);

    protected override int BatchSize => 500;

    protected override TimeSpan CheckInterval => TimeSpan.FromMinutes(30);

    protected override async Task<int> CleanupAsync(
        DbContext dataContext,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var retentionDate = now.AddDays(-7);

        var outboxMessages = dataContext.Set<OutboxMessage>();

        var ids = await outboxMessages
            .Where(x => x.ProcessedAt != null && x.ProcessedAt <= retentionDate)
            .OrderBy(x => x.ProcessedAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
            return 0;

        return await outboxMessages
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}