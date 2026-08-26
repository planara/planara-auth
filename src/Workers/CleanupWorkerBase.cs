using Microsoft.EntityFrameworkCore;
using Planara.Auth.Data;

namespace Planara.Auth.Workers;

public abstract class CleanupWorkerBase(ILogger logger, IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected virtual int BatchSize => 100;

    protected virtual TimeSpan CheckInterval => TimeSpan.FromMinutes(5);

    protected virtual TimeSpan ErrorInterval => TimeSpan.FromSeconds(30);

    protected abstract string WorkerName { get; }

    protected abstract Task<int> CleanupAsync(
        DataContext dataContext,
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                var dataContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                var executionStrategy = dataContext.Database.CreateExecutionStrategy();

                var deletedCount =
                    await executionStrategy.ExecuteAsync(
                        async () =>
                        {
                            await using var transaction = await dataContext.Database.BeginTransactionAsync(cancellationToken);

                            var deleted = await CleanupAsync(dataContext, DateTime.UtcNow, BatchSize, cancellationToken);

                            await transaction.CommitAsync(cancellationToken);

                            return deleted;
                        });

                if (deletedCount > 0)
                {
                    logger.LogInformation("[{WorkerName}]: Deleted {Count} records.", WorkerName, deletedCount);
                }
                
                if (deletedCount >= BatchSize)
                    continue;

                await Task.Delay(CheckInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "[{WorkerName}]: Cleanup failed.", WorkerName);

                await Task.Delay(ErrorInterval, cancellationToken);
            }
        }
    }
}