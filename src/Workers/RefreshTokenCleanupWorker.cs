using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;

namespace Planara.Auth.Workers;

public sealed class RefreshTokenCleanupWorker(
    ILogger<RefreshTokenCleanupWorker> logger,
    IServiceScopeFactory scopeFactory)
    : CleanupWorkerBase(logger, scopeFactory)
{
    protected override string WorkerName => nameof(RefreshTokenCleanupWorker);

    protected override int BatchSize => 500;

    protected override TimeSpan CheckInterval => TimeSpan.FromMinutes(30);

    protected override async Task<int> CleanupAsync(DataContext dataContext, DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dataContext.RefreshTokens
            .Where(x => x.ExpiresAtUtc <= now)
            .OrderBy(x => x.ExpiresAtUtc)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
            return 0;

        return await dataContext.RefreshTokens
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}