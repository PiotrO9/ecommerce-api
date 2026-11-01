using Microsoft.EntityFrameworkCore;
using E_commerce_backend.Data;

namespace E_commerce_backend.Services.Pricing;

public static class CatalogWriteLock
{
    // Also acquired by database statement triggers before catalog writes.
    // A single lock gives a common order for legacy SQL, publication and price commands.
    public static Task AcquireAsync(AppDbContext database, CancellationToken ct) =>
        database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20261031, 3)", ct);
}
