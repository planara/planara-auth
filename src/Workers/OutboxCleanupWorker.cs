using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;

namespace Planara.Auth.Workers;

public sealed class OutboxCleanupWorker(ILogger<OutboxCleanupWorker> logger, IServiceScopeFactory scopeFactory) : CleanupWorkerBase(logger, scopeFactory)
{
    protected override string WorkerName => nameof(OutboxCleanupWorker);

    protected override int BatchSize => 500;

    protected override TimeSpan CheckInterval => TimeSpan.FromMinutes(30);

    protected override async Task<int> CleanupAsync(DataContext dataContext, DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var retentionDate = now.AddDays(-7);

        var ids = await dataContext.OutboxMessages
            .Where(x => x.ProcessedAt != null && x.ProcessedAt <= retentionDate)
            .OrderBy(x => x.ProcessedAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
            return 0;

        return await dataContext.OutboxMessages
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}