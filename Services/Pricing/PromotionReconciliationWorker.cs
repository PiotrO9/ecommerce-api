using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public sealed class PromotionReconciliationWorker(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<PromotionReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await RunOnceAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Nie udało się uzgodnić dynamicznych promocji. Kolejna próba za 5 sekund."); }
        }
    }

    public static async Task RunOnceAsync(AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(db, ct);
        var job = await db.PromotionReconciliations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (job is null || !job.Dirty && job.NextAttemptAt > clock.GetUtcNow().UtcDateTime) return;
        await new DynamicPromotionEngine(db, clock).ReconcileAsync(ct);
        await tx.CommitAsync(ct);
    }
}
