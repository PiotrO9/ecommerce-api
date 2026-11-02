using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed class VariantStockService(AppDbContext db, CatalogCommandStore store, TimeProvider clock)
{
    public Task<CatalogResult> SetAsync(Guid id, StockSetRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var reason = CatalogInput.Text(r.Reason, 120, "reason")!;
        if (r.QuantityOnHand < 0 || r.ExpectedVersion is < 1) throw new CatalogValidation("invalid_stock");
        return store.ExecuteAsync("stock.set", id, r.ExpectedVersion, new { r.QuantityOnHand, reason }, actor, key, async () =>
        {
            if (!await db.ProductVariants.AnyAsync(v => v.Id == id, ct)) throw new CatalogValidation("variant_not_found", status: 404);
            var stock = await db.VariantStocks.AsNoTracking().SingleOrDefaultAsync(s => s.VariantId == id, ct);
            var reserved = await E_commerce_backend.Services.Inventory.InventoryAvailability.ReservedAsync(db, [id],
                await E_commerce_backend.Services.Inventory.InventoryAvailability.NowAsync(db, ct), ct);
            if (r.QuantityOnHand < reserved.GetValueOrDefault(id)) throw new CatalogValidation("stock_below_reservations", status: 409);
            var before = stock?.QuantityOnHand;
            if (stock is null)
            {
                if (r.ExpectedVersion is not null) throw new CatalogValidation("version_conflict", status: 409);
                var now = clock.GetUtcNow().UtcDateTime;
                stock = new() { VariantId = id, QuantityOnHand = r.QuantityOnHand, CreatedAt = now, UpdatedAt = now };
                db.VariantStocks.Add(stock); await db.SaveChangesAsync(ct);
            }
            else
            {
                if (r.ExpectedVersion is null) throw new CatalogValidation("version_conflict", status: 409);
                CatalogCommandStore.Version(stock.Version, r.ExpectedVersion.Value);
                await db.VariantStocks.Where(s => s.Id == stock.Id).ExecuteUpdateAsync(s => s.SetProperty(s => s.QuantityOnHand, r.QuantityOnHand), ct);
            }
            var current = await db.VariantStocks.AsNoTracking().SingleAsync(s => s.VariantId == id, ct);
            return new(new(false, current.Id, current.Version, VariantId: id), before is null ? 201 : 200, reason, before, r.QuantityOnHand);
        }, ct);
    }
}
