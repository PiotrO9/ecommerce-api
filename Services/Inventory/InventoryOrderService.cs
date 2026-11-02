using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Inventory;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using E_commerce_backend.Services.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace E_commerce_backend.Services.Inventory;

public sealed class InventoryOrderService(AppDbContext db, TimeProvider time, IOptions<InventoryOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ShoppingResult> CreateAsync(Guid userId, Guid operationKey, OrderCreateRequest request, CancellationToken ct)
    {
        if (userId == Guid.Empty || operationKey == Guid.Empty || request.ExpectedScopeId == Guid.Empty || request.ExpectedVersion < 0 ||
            request.Items is not { Count: > 0 and <= 100 } || request.Items.Any(i => i is null || i.VariantId == Guid.Empty ||
                i.Quantity is < 1 or > 99 || i.PriceChangeId == Guid.Empty || i.UnitPrice < 0 || decimal.Round(i.UnitPrice, 2) != i.UnitPrice) ||
            request.Items.Select(i => i.VariantId).Distinct().Count() != request.Items.Count)
            return Task.FromResult(Error(400, "invalid_order_request"));

        var accepted = request.Items.OrderBy(i => i.VariantId).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { format = "inventory/v1", request.ExpectedScopeId, request.ExpectedVersion, items = accepted }, Json))));
        return WriteAsync(async () =>
        {
            var old = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.UserId == userId && o.OperationKey == operationKey, ct);
            if (old is not null)
                return old.RequestHash == hash ? new(200, (await ReadAsync([old.Id], ct))[0] with { Replayed = true }) : Error(409, "idempotency_key_conflict");

            await ShoppingWriteLock.SharedAsync(db, ct);
            var cart = await new ShoppingStore(db, time).CartAsync(new(userId, null), true, ct);
            if (cart is null || cart.Id != request.ExpectedScopeId) return Error(409, "shopping_scope_changed");
            if (cart.CartVersion != request.ExpectedVersion) return Error(409, "shopping_version_conflict");
            var cartItems = await db.CartItems.AsNoTracking().Where(i => i.ShoppingStateId == cart.Id).ToArrayAsync(ct);
            if (cartItems.Length != accepted.Length || accepted.Any(a => !cartItems.Any(i => i.VariantId == a.VariantId && i.Quantity == a.Quantity)))
                return Error(409, "cart_changed");

            var ids = accepted.Select(i => i.VariantId).ToArray();
            await LockStocksAsync(ids, ct);
            var now = await InventoryAvailability.NowAsync(db, ct); // Evaluate after waiting for locks.
            var offers = await new ShoppingOfferReader(db).ReadAsync(ids, now, ct);
            foreach (var item in accepted)
            {
                var offer = offers.GetValueOrDefault(item.VariantId);
                var availability = ShoppingOfferReader.Availability(offer, item.Quantity);
                if (availability != "available") return Error(409, availability);
                if (offer!.PriceChangeId != item.PriceChangeId || offer.Price.Amount != item.UnitPrice) return Error(409, "price_changed");
            }
            var total = accepted.Sum(i => i.UnitPrice * i.Quantity);
            if (total > 9999999999999999.99m) return Error(400, "order_total_too_large");
            var order = new InventoryOrder
            {
                UserId = userId, OperationKey = operationKey, RequestHash = hash, GoodsTotal = total, CreatedAt = now, UpdatedAt = now
            };
            var reservation = new StockReservation
            {
                OrderId = order.Id, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddMinutes(options.Value.ReservationMinutes)
            };
            db.Orders.Add(order);
            db.StockReservations.Add(reservation);
            foreach (var item in accepted)
            {
                var offer = offers[item.VariantId];
                db.OrderItems.Add(new()
                {
                    OrderId = order.Id, VariantId = item.VariantId, Quantity = item.Quantity, UnitPrice = item.UnitPrice,
                    Name = offer.Name, Sku = offer.Sku, PriceChangeId = item.PriceChangeId, AttributesJson = JsonSerializer.Serialize(offer.Attributes, Json)
                });
                db.StockReservationItems.Add(new() { ReservationId = reservation.Id, VariantId = item.VariantId, Quantity = item.Quantity });
            }
            await db.SaveChangesAsync(ct);
            await db.CartItems.Where(i => i.ShoppingStateId == cart.Id).ExecuteDeleteAsync(ct);
            return new(201, (await ReadAsync([order.Id], ct))[0]);
        }, ct);
    }

    public async Task<ShoppingResult> GetAsync(Guid userId, Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await db.Orders.AnyAsync(o => o.Id == id && o.UserId == userId, ct)) return Error(404, "order_not_found");
        return new(200, (await ReadAsync([id], ct))[0]);
    }

    public async Task<ShoppingResult> ListAsync(Guid userId, int page, int size, CancellationToken ct)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue) return Error(400, "invalid_pagination");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(ct);
        var ids = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * size).Take(size).Select(o => o.Id).ToArrayAsync(ct);
        return new(200, new OrderPage(await ReadAsync(ids, ct), page, size, total));
    }

    public Task<ShoppingResult> CancelAsync(Guid userId, Guid id, CancellationToken ct) => WriteAsync(async () =>
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);
        if (order is null) return Error(404, "order_not_found");
        var reservation = await db.StockReservations.SingleAsync(r => r.OrderId == id, ct);
        var now = await InventoryAvailability.NowAsync(db, ct);
        if (order.Status == "Cancelled") return new(200, (await ReadAsync([id], ct))[0]);
        if (order.Status != "PendingPayment") return Error(409, "order_not_cancellable");
        var expired = reservation.ExpiresAt <= now;
        order.Status = expired ? "Expired" : "Cancelled";
        reservation.Status = expired ? "Expired" : "Released";
        order.UpdatedAt = reservation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return new(200, (await ReadAsync([id], ct))[0]);
    }, ct);

    // No HTTP endpoint: the future payment adapter must first authenticate the provider event.
    public Task<ShoppingResult> ConfirmPaymentAsync(Guid id, Guid eventId, decimal amount, string currency, CancellationToken ct)
    {
        if (eventId == Guid.Empty || amount < 0 || currency != "PLN") return Task.FromResult(Error(400, "invalid_payment_event"));
        return WriteAsync(async () =>
        {
            var receipt = await db.InventoryPaymentReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.EventId == eventId, ct);
            if (receipt is not null)
                return receipt.OrderId == id && receipt.Amount == amount && receipt.Currency == currency
                    ? new(200, (await ReadAsync([id], ct))[0] with { Replayed = true }) : Error(409, "payment_event_conflict");
            var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
            if (order is null) return Error(404, "order_not_found");
            if (order.GoodsTotal != amount || order.Currency != currency) return Error(409, "payment_amount_mismatch");
            var reservation = await db.StockReservations.SingleAsync(r => r.OrderId == id, ct);
            var items = await db.StockReservationItems.AsNoTracking().Where(i => i.ReservationId == reservation.Id).OrderBy(i => i.VariantId).ToArrayAsync(ct);
            var stocks = await LockStocksAsync(items.Select(i => i.VariantId).ToArray(), ct);
            var now = await InventoryAvailability.NowAsync(db, ct);
            if (order.Status == "PendingPayment" && reservation.Status == "Active" && reservation.ExpiresAt > now)
            {
                if (items.Any(i => !stocks.TryGetValue(i.VariantId, out var s) || s.QuantityOnHand < i.Quantity)) return Error(409, "insufficient_stock");
                order.Status = "Paid";
                reservation.Status = "Consumed";
                order.UpdatedAt = reservation.UpdatedAt = now;
                // Stop counting this reservation before reducing stock.
                await db.SaveChangesAsync(ct);
                foreach (var item in items)
                {
                    var stock = stocks[item.VariantId];
                    await db.VariantStocks.Where(s => s.Id == stock.Id).ExecuteUpdateAsync(s => s.SetProperty(s => s.QuantityOnHand, stock.QuantityOnHand - item.Quantity), ct);
                    db.StockMovements.Add(new()
                    {
                        ReservationId = reservation.Id, VariantId = item.VariantId, QuantityBefore = stock.QuantityOnHand,
                        QuantityAfter = stock.QuantityOnHand - item.Quantity, CreatedAt = now
                    });
                }
            }
            else if (order.Status != "Paid")
            {
                // A late charge must never consume stock released for another buyer.
                order.Status = "PaymentReview";
                if (reservation.Status == "Active") reservation.Status = "Expired";
                order.UpdatedAt = reservation.UpdatedAt = now;
            }
            db.InventoryPaymentReceipts.Add(new()
            {
                EventId = eventId, OrderId = id, Amount = amount, Currency = currency, Outcome = order.Status, CreatedAt = now
            });
            await db.SaveChangesAsync(ct);
            return new(200, (await ReadAsync([id], ct))[0]);
        }, ct);
    }

    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        var count = 0;
        var result = await WriteAsync(async () =>
        {
            var now = await InventoryAvailability.NowAsync(db, ct);
            var reservations = await db.StockReservations.Where(r => r.Status == "Active" && r.ExpiresAt <= now)
                .OrderBy(r => r.ExpiresAt).ThenBy(r => r.Id).Take(100).ToArrayAsync(ct);
            var ids = reservations.Select(r => r.OrderId).ToArray();
            var orders = await db.Orders.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
            foreach (var reservation in reservations)
            {
                reservation.Status = "Expired";
                reservation.UpdatedAt = now;
                var order = orders[reservation.OrderId];
                order.Status = "Expired";
                order.UpdatedAt = now;
            }
            count = reservations.Length;
            return new(200);
        }, ct);
        if (result.Status != 200) throw new InvalidOperationException("Nie udało się zatwierdzić wygaszania rezerwacji.");
        return count;
    }

    private async Task<Dictionary<Guid, VariantStock>> LockStocksAsync(Guid[] ids, CancellationToken ct) =>
        await db.VariantStocks.FromSqlInterpolated($"SELECT * FROM app.variant_stock WHERE variant_id = ANY({ids}) ORDER BY variant_id FOR UPDATE")
            .AsNoTracking().ToDictionaryAsync(s => s.VariantId, ct);

    private async Task<OrderResponse[]> ReadAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var reservations = await db.StockReservations.AsNoTracking().Where(r => ids.Contains(r.OrderId)).ToDictionaryAsync(r => r.OrderId, ct);
        var items = await db.OrderItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).OrderBy(i => i.VariantId).ToArrayAsync(ct);
        var now = await InventoryAvailability.NowAsync(db, ct);
        return ids.Select(id =>
        {
            var o = orders[id]; var r = reservations[id];
            var expired = o.Status == "PendingPayment" && r.ExpiresAt <= now;
            return new OrderResponse(o.Id, expired ? "Expired" : o.Status, o.CreatedAt, r.ExpiresAt,
                expired ? "Expired" : r.Status, o.GoodsTotal, o.Currency,
                items.Where(i => i.OrderId == id).Select(i => new OrderItemResponse(i.VariantId, i.Name, i.Sku, i.Quantity, i.UnitPrice)).ToArray());
        }).ToArray();
    }

    private async Task<ShoppingResult> WriteAsync(Func<Task<ShoppingResult>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Existing catalog triggers require this lock before any row/shopping lock.
            await CatalogWriteLock.AcquireAsync(db, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException p && IsConflict(p)) { return Error(409, "inventory_conflict"); }
        catch (PostgresException e) when (IsConflict(e)) { return Error(409, "inventory_conflict"); }
        finally { db.ChangeTracker.Clear(); }
    }

    private static bool IsConflict(PostgresException e) => e.SqlState is "23505" or "23503" or "23514" or "40001" or "40P01";
    private static ShoppingResult Error(int status, string code) => ShoppingResult.Error(status, code, code switch
    {
        "price_changed" => "Cena uległa zmianie. Odczytaj koszyk i zaakceptuj aktualną cenę.",
        "order_not_found" => "Nie znaleziono zamówienia.",
        "stock_unknown" or "out_of_stock" or "insufficient_stock" => "Brak wystarczającego, potwierdzonego stanu magazynowego.",
        _ => "Sprawdź dane żądania i aktualny stan zamówienia lub koszyka."
    });
}
