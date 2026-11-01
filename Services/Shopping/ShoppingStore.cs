using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public sealed class ShoppingStore(AppDbContext db, TimeProvider time)
{
    public async Task EnsureAccountCartAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var id = Guid.NewGuid(); var now = time.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.shopping_states(id,user_id,cart_version,created_at,updated_at)
            VALUES ({id},{userId},0,{now},{now})
            ON CONFLICT (user_id) WHERE user_id IS NOT NULL DO NOTHING
            """, ct);
        await tx.CommitAsync(ct);
    }

    public async Task EnsureFavoritesAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var id = Guid.NewGuid(); var now = time.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.favorite_lists(user_id,scope_id,version,created_at,updated_at)
            VALUES ({userId},{id},0,{now},{now}) ON CONFLICT (user_id) DO NOTHING
            """, ct);
        await tx.CommitAsync(ct);
    }

    public Task<ShoppingState?> CartAsync(ShoppingOwner owner, bool forUpdate, CancellationToken ct)
    {
        IQueryable<ShoppingState> query;
        if (forUpdate)
            query = owner.UserId is Guid userId
                ? db.ShoppingStates.FromSqlInterpolated($"SELECT * FROM app.shopping_states WHERE user_id={userId} FOR UPDATE")
                : db.ShoppingStates.FromSqlInterpolated($"SELECT * FROM app.shopping_states WHERE user_id IS NULL AND guest_token_hash={owner.GuestTokenHash} FOR UPDATE");
        else query = owner.UserId is Guid userId
            ? db.ShoppingStates.Where(s => s.UserId == userId)
            : db.ShoppingStates.Where(s => s.UserId == null && s.GuestTokenHash == owner.GuestTokenHash);
        return query.AsNoTracking().SingleOrDefaultAsync(ct);
    }

    public bool IsExpired(ShoppingState state) => state.UserId is null && state.ExpiresAt <= time.GetUtcNow().UtcDateTime;
}
