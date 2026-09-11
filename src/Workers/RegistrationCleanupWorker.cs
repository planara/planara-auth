using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data.Domain;
using Planara.Common.Workers;

namespace Planara.Auth.Workers;

public sealed class RegistrationCleanupWorker(ILogger<RegistrationCleanupWorker> logger, IServiceScopeFactory scopeFactory) : CleanupWorkerBase(logger, scopeFactory)
{
    protected override string WorkerName => nameof(RegistrationCleanupWorker);

    protected override TimeSpan CheckInterval => TimeSpan.FromMinutes(5);

    protected override async Task<int> CleanupAsync(
        DbContext dataContext,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var registrationSessions =
            dataContext.Set<RegistrationSession>();

        var ids = await registrationSessions
            .Where(x => x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => x.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (ids.Length == 0)
            return 0;

        return await registrationSessions
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}