using System.Data;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public sealed class FavoritesService(AppDbContext db, ShoppingStore store, ShoppingOfferReader offers, TimeProvider time)
{
    public async Task<ShoppingResult> GetAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ShoppingResult.Error(400, "invalid_pagination", "Niepoprawne stronicowanie ulubionych.");
        await store.EnsureFavoritesAsync(userId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var list = await db.FavoriteLists.AsNoTracking().SingleAsync(l => l.UserId == userId, ct);
        var query = db.FavoriteItems.AsNoTracking().Where(i => i.UserId == userId);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.VariantId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var now = await E_commerce_backend.Services.Inventory.InventoryAvailability.NowAsync(db, ct);
        var variants = await offers.ReadAsync(rows.Select(i => i.VariantId).ToArray(), now, ct);
        var items = rows.Select(i => new FavoriteItemResponse(i.VariantId, i.CreatedAt,
            ShoppingOfferReader.Availability(variants.GetValueOrDefault(i.VariantId)), variants.GetValueOrDefault(i.VariantId))).ToArray();
        await tx.CommitAsync(ct);
        return new(200, new FavoritesResponse(list.ScopeId, list.Version, now, items, page, pageSize, total, (total + pageSize - 1) / pageSize));
    }

    public async Task<ShoppingResult> MutateAsync(Guid userId, Guid variantId, ShoppingVersionRequest stamp, bool add, CancellationToken ct)
    {
        if (variantId == Guid.Empty || !CartService.Valid(stamp))
            return ShoppingResult.Error(400, "invalid_favorites_request", "Podaj poprawny wariant, zakres i wersję listy.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var list = await db.FavoriteLists.FromSqlInterpolated($"SELECT * FROM app.favorite_lists WHERE user_id={userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (list is null || list.ScopeId != stamp.ExpectedScopeId)
            return ShoppingResult.Error(409, "shopping_scope_changed", "Odczytaj ulubione aktualnego konta.");
        if (list.Version != stamp.ExpectedVersion)
            return ShoppingResult.Error(409, "shopping_version_conflict", "Lista została zmieniona. Odczytaj ulubione.");
        var exists = await db.FavoriteItems.AnyAsync(i => i.UserId == userId && i.VariantId == variantId, ct);
        if (add && !exists)
        {
            var variants = await offers.ReadAsync([variantId], time.GetUtcNow().UtcDateTime, ct);
            if (!variants.ContainsKey(variantId)) return ShoppingResult.Error(404, "variant_unavailable", "Wariant jest niedostępny.");
            if (await db.FavoriteItems.CountAsync(i => i.UserId == userId, ct) >= 500)
                return ShoppingResult.Error(409, "favorites_limit", "Lista może zawierać najwyżej 500 wariantów.");
            var now = time.GetUtcNow().UtcDateTime;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.favorite_items(user_id,variant_id,created_at) VALUES ({userId},{variantId},{now})", ct);
        }
        else if (!add && exists)
            await db.FavoriteItems.Where(i => i.UserId == userId && i.VariantId == variantId).ExecuteDeleteAsync(ct);
        var version = await db.FavoriteLists.Where(l => l.UserId == userId).Select(l => l.Version).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return new(200, new ShoppingVersionResponse(list.ScopeId, version));
    }
}
