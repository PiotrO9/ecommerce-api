using System.Data;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public sealed class CartService(AppDbContext db, ShoppingStore store, ShoppingOfferReader offers, TimeProvider time)
{
    public async Task<ShoppingResult> GetAsync(ShoppingOwner owner, CancellationToken ct)
    {
        if (owner.UserId is null && owner.GuestTokenHash is null) return Empty();
        if (owner.UserId is Guid userId) await store.EnsureAccountCartAsync(userId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var state = await store.CartAsync(owner, false, ct);
        if (state is null || store.IsExpired(state)) return Empty();
        var now = await E_commerce_backend.Services.Inventory.InventoryAvailability.NowAsync(db, ct);
        var rows = await db.CartItems.AsNoTracking().Where(i => i.ShoppingStateId == state.Id)
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.VariantId).ToArrayAsync(ct);
        var variants = await offers.ReadAsync(rows.Select(i => i.VariantId).ToArray(), now, ct);
        var items = rows.Select(i =>
        {
            var variant = variants.GetValueOrDefault(i.VariantId);
            return new CartItemResponse(i.VariantId, i.Quantity, ShoppingOfferReader.Availability(variant, i.Quantity),
                variant, variant is null ? null : variant.Price.Amount * i.Quantity);
        }).ToArray();
        var blocked = items.Count(i => i.Availability != "available");
        var subtotal = items.Where(i => i.Availability == "available").Sum(i => i.LineAmount!.Value);
        await tx.CommitAsync(ct);
        return new(200, new CartResponse(state.Id, state.CartVersion, now, "PLN", items, subtotal, blocked, items.Length > 0 && blocked == 0));
    }

    public async Task<ShoppingResult> MutateAsync(ShoppingOwner owner, Guid? variantId, int? quantity,
        ShoppingVersionRequest stamp, bool clear, CancellationToken ct)
    {
        if (!Valid(stamp) || (!clear && (variantId is null || variantId == Guid.Empty)) || quantity is < 1 or > 99)
            return ShoppingResult.Error(400, "invalid_cart_request", "Podaj poprawny wariant, ilość, zakres i wersję koszyka.");
        if (owner.UserId is null && owner.GuestTokenHash is null) return SessionRequired();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ShoppingWriteLock.SharedAsync(db, ct);
        var state = await store.CartAsync(owner, true, ct);
        if (state is null || store.IsExpired(state)) return SessionRequired();
        if (state.Id != stamp.ExpectedScopeId) return ShoppingResult.Error(409, "shopping_scope_changed", "Zmieniono właściciela koszyka. Odczytaj aktualny wybór.");
        if (state.CartVersion != stamp.ExpectedVersion) return ShoppingResult.Error(409, "shopping_version_conflict", "Koszyk został zmieniony. Odczytaj aktualny wybór.");
        if (clear)
            await db.CartItems.Where(i => i.ShoppingStateId == state.Id).ExecuteDeleteAsync(ct);
        else
        {
            var current = await db.CartItems.AsNoTracking().SingleOrDefaultAsync(i => i.ShoppingStateId == state.Id && i.VariantId == variantId, ct);
            if (quantity is int requested)
            {
                if (current is null || requested > current.Quantity)
                {
                    var available = await offers.ReadAsync([variantId!.Value],
                        await E_commerce_backend.Services.Inventory.InventoryAvailability.NowAsync(db, ct), ct);
                    var offer = available.GetValueOrDefault(variantId.Value);
                    if (offer is null) return ShoppingResult.Error(404, "variant_unavailable", "Wariant jest niedostępny.");
                    var availability = ShoppingOfferReader.Availability(offer, requested);
                    if (availability != "available") return ShoppingResult.Error(409, availability, "Brak wystarczającego, potwierdzonego stanu magazynowego.");
                    if (current is null && await db.CartItems.CountAsync(i => i.ShoppingStateId == state.Id, ct) >= 100)
                        return ShoppingResult.Error(409, "cart_limit", "Koszyk może zawierać najwyżej 100 wariantów.");
                }
                if (current?.Quantity != requested)
                {
                    var now = time.GetUtcNow().UtcDateTime;
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO app.cart_items(shopping_state_id,variant_id,quantity,created_at,updated_at)
                        VALUES ({state.Id},{variantId!.Value},{requested},{now},{now})
                        ON CONFLICT (shopping_state_id,variant_id) DO UPDATE SET quantity=EXCLUDED.quantity,updated_at=EXCLUDED.updated_at
                        """, ct);
                }
            }
            else if (current is not null)
                await db.CartItems.Where(i => i.ShoppingStateId == state.Id && i.VariantId == variantId).ExecuteDeleteAsync(ct);
        }
        var version = await db.ShoppingStates.Where(s => s.Id == state.Id).Select(s => s.CartVersion).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return new(200, new ShoppingVersionResponse(state.Id, version));
    }

    public static bool Valid(ShoppingVersionRequest? stamp) => stamp?.ExpectedScopeId is Guid id && id != Guid.Empty && stamp.ExpectedVersion is >= 0;
    private static ShoppingResult SessionRequired() => ShoppingResult.Error(409, "guest_session_expired", "Odczytaj koszyk; dla gościa zainicjuj nową sesję.");
    private ShoppingResult Empty() => new(200, new CartResponse(null, 0, time.GetUtcNow().UtcDateTime, "PLN", [], 0, 0, false));
}
