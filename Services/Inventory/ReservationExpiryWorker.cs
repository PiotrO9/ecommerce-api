using E_commerce_backend.Contracts.Inventory;
using Microsoft.Extensions.Options;

namespace E_commerce_backend.Services.Inventory;

public sealed class ReservationExpiryWorker(IServiceScopeFactory scopes, IOptions<InventoryOptions> options,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup must remain possible before deployment applies a new migration.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ExpiryIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<InventoryOrderService>().ExpireAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogError(e, "Nie udało się wygasić rezerwacji magazynowych."); }
        }
    }
}
