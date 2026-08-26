using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;

namespace Planara.Auth.Workers;

public sealed class RegistrationCleanupWorker(ILogger<RegistrationCleanupWorker> logger, IServiceScopeFactory scopeFactory) : CleanupWorkerBase(logger, scopeFactory)
{
    protected override string WorkerName => nameof(RegistrationCleanupWorker);

    protected override TimeSpan CheckInterval => TimeSpan.FromMinutes(5);

    protected override async Task<int> CleanupAsync(DataContext dataContext, DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dataContext.RegistrationSessions
            .Where(x => x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
            return 0;

        return await dataContext.RegistrationSessions
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}