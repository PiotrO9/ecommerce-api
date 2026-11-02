using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Inventory;

public static class InventoryAvailability
{
    // One authoritative UTC clock for reservation deadlines, including after lock waits.
    public static Task<DateTime> NowAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    public static Task<Dictionary<Guid, int>> ReservedAsync(AppDbContext db, Guid[] variantIds, DateTime now, CancellationToken ct) =>
        (from item in db.StockReservationItems.AsNoTracking()
         join reservation in db.StockReservations on item.ReservationId equals reservation.Id
         where variantIds.Contains(item.VariantId) && reservation.Status == "Active" && reservation.ExpiresAt > now
         group item by item.VariantId into items
         select new { VariantId = items.Key, Quantity = items.Sum(i => i.Quantity) })
        .ToDictionaryAsync(x => x.VariantId, x => x.Quantity, ct);
}
