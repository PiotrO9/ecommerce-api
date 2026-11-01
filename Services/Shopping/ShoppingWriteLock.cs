using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public static class ShoppingWriteLock
{
    // Commands for different owners can proceed together. The demo reset takes
    // the exclusive form before changing versions and truncating catalog items.
    public static Task SharedAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(20261101, 4)", ct);
    public static Task ExclusiveAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20261101, 4)", ct);
}
