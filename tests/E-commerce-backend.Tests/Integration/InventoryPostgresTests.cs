using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Contracts.Inventory;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Catalog.Management;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Inventory;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class InventoryPostgresTests
{
    [PostgresFact]
    public async Task LastUnitHasOneWinnerAndLosingCartIsPreserved()
    {
        await using var s = await InventoryScenario.Create();
        await using (var db = ShoppingScenario.Open())
            await db.VariantStocks.Where(x => x.VariantId == s.Shopping.Cap).ExecuteUpdateAsync(x => x.SetProperty(v => v.QuantityOnHand, 1));
        var a = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1));
        var b = await s.Request(s.Shopping.AccountB, (s.Shopping.Cap, 1));
        var results = await Task.WhenAll(s.CreateOrder(s.Shopping.UserA, a), s.CreateOrder(s.Shopping.UserB, b));
        Assert.Equal([201, 409], results.Select(r => r.Status).Order());
        Assert.Equal("out_of_stock", Assert.IsType<ShoppingError>(results.Single(r => r.Status == 409).Body).Code);
        await using var check = ShoppingScenario.Open();
        Assert.Equal(1, await check.Orders.CountAsync());
        Assert.Equal(1, await check.StockReservations.CountAsync());
        Assert.Equal(1, await check.CartItems.CountAsync());
        Assert.Equal(1, await check.VariantStocks.Where(x => x.VariantId == s.Shopping.Cap).Select(x => x.QuantityOnHand).SingleAsync());
    }

    [PostgresFact]
    public async Task ConcurrentReplayDoesNotReserveTwiceOrExtendDeadlineAndPricesAreAcceptedExplicitly()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 2));
        var wrong = request with { Items = request.Items!.Select(i => i with { UnitPrice = i.UnitPrice + 1 }).ToArray() };
        var rejected = await s.CreateOrder(s.Shopping.UserA, wrong);
        Assert.Equal("price_changed", Assert.IsType<ShoppingError>(rejected.Body).Code);
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(s.CreateOrder(s.Shopping.UserA, request, key), s.CreateOrder(s.Shopping.UserA, request, key));
        Assert.Equal([200, 201], results.Select(r => r.Status).Order());
        var first = Assert.IsType<OrderResponse>(results.Single(r => r.Status == 201).Body);
        var replay = Assert.IsType<OrderResponse>(results.Single(r => r.Status == 200).Body);
        Assert.True(replay.Replayed); Assert.Equal(first.Id, replay.Id); Assert.Equal(first.ExpiresAt, replay.ExpiresAt);
        Assert.Empty((await s.Shopping.Cart(s.Shopping.AccountA)).Items);
        var conflict = await s.CreateOrder(s.Shopping.UserA, wrong, key);
        Assert.Equal("idempotency_key_conflict", Assert.IsType<ShoppingError>(conflict.Body).Code);
        await using var db = ShoppingScenario.Open();
        var offers = await new ShoppingOfferReader(db).ReadAsync([s.Shopping.Cap], DateTime.UtcNow, default);
        Assert.Equal(3, offers[s.Shopping.Cap].QuantityOnHand); Assert.Equal(1, offers[s.Shopping.Cap].QuantityAvailable);
        var stock = Assert.IsType<AdminStockResponse>((await new CatalogManagementReader(db, TimeProvider.System).StockAsync(s.Shopping.Cap, default)).Body).Stock!;
        Assert.Equal(2, stock.QuantityReserved); Assert.Equal(1, stock.QuantityAvailable);
        var product = await db.ProductVariants.Where(v => v.Id == s.Shopping.Cap).Select(v => v.ProductId).SingleAsync();
        var details = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(await new ProductsController(db).Get(product, null, default)).Value);
        Assert.Equal(1, Assert.Single(details.Variants).QuantityAvailable);
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Empty(await db.StockMovements.ToArrayAsync());
    }

    [PostgresFact]
    public async Task MultiItemFailureAndDatabaseFailureRollbackOrderReservationAndCartChanges()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1), (s.Shopping.Red, 2));
        await using var db = ShoppingScenario.Open();
        await db.VariantStocks.Where(v => v.VariantId == s.Shopping.Red).ExecuteUpdateAsync(v => v.SetProperty(x => x.QuantityOnHand, 1));
        Assert.Equal(409, (await s.CreateOrder(s.Shopping.UserA, request)).Status);
        Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(2, await db.CartItems.CountAsync());
        await db.VariantStocks.Where(v => v.VariantId == s.Shopping.Red).ExecuteUpdateAsync(v => v.SetProperty(x => x.QuantityOnHand, 5));
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION app.test_inventory_failure() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic reservation failure' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER test_inventory_failure AFTER INSERT ON app.orders DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION app.test_inventory_failure();
            """);
        try { Assert.Equal(409, (await s.CreateOrder(s.Shopping.UserA, request)).Status); }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_inventory_failure ON app.orders; DROP FUNCTION app.test_inventory_failure();");
        }
        Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(0, await db.StockReservations.CountAsync());
        Assert.Equal(2, await db.CartItems.CountAsync());
    }

    [PostgresFact]
    public async Task PaymentConsumesOnceAndChecksAmountAndEventOwnership()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 2), (s.Shopping.Red, 1));
        var order = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA, request)).Body);
        var key = Guid.NewGuid();
        await using (var db = ShoppingScenario.Open())
            Assert.Equal(409, (await InventoryScenario.Service(db).ConfirmPaymentAsync(order.Id, key, order.GoodsTotal + 1, "PLN", default)).Status);
        async Task<ShoppingResult> Pay(Guid? eventKey = null)
        {
            await using var db = ShoppingScenario.Open();
            return await InventoryScenario.Service(db).ConfirmPaymentAsync(order.Id, eventKey ?? key, order.GoodsTotal, "PLN", default);
        }
        var race = await Task.WhenAll(Pay(), Pay());
        Assert.All(race, r => Assert.Equal("Paid", Assert.IsType<OrderResponse>(r.Body).Status));
        Assert.Single(race, r => Assert.IsType<OrderResponse>(r.Body).Replayed);
        Assert.Equal("Paid", Assert.IsType<OrderResponse>((await Pay(Guid.NewGuid())).Body).Status);
        await using var check = ShoppingScenario.Open();
        Assert.Equal(1, await check.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).Select(v => v.QuantityOnHand).SingleAsync());
        Assert.Equal(4, await check.VariantStocks.Where(v => v.VariantId == s.Shopping.Red).Select(v => v.QuantityOnHand).SingleAsync());
        Assert.Equal(2, await check.StockMovements.CountAsync());
        Assert.Equal(409, (await InventoryScenario.Service(check).CancelAsync(s.Shopping.UserA, order.Id, default)).Status);
        Assert.Equal(409, (await InventoryScenario.Service(check).ConfirmPaymentAsync(Guid.NewGuid(), key, order.GoodsTotal, "PLN", default)).Status);
    }

    [PostgresFact]
    public async Task ExpiredReservationStopsBlockingBeforeWorkerAndLatePaymentNeverConsumesNewBuyersStock()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 3));
        var old = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA, request)).Body);
        await s.MakeExpired(old.Id);
        await using (var db = ShoppingScenario.Open())
        {
            var shown = Assert.IsType<OrderResponse>((await InventoryScenario.Service(db).GetAsync(s.Shopping.UserA, old.Id, default)).Body);
            Assert.Equal("Expired", shown.Status);
            Assert.Equal("Active", await db.StockReservations.Select(r => r.Status).SingleAsync());
        }
        var nextRequest = await s.Request(s.Shopping.AccountB, (s.Shopping.Cap, 3));
        var next = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserB, nextRequest)).Body);
        await using var check = ShoppingScenario.Open();
        var result = await InventoryScenario.Service(check).ConfirmPaymentAsync(old.Id, Guid.NewGuid(), old.GoodsTotal, "PLN", default);
        Assert.Equal("PaymentReview", Assert.IsType<OrderResponse>(result.Body).Status);
        Assert.Equal(3, await check.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).Select(v => v.QuantityOnHand).SingleAsync());
        Assert.Empty(await check.StockMovements.ToArrayAsync());
        Assert.Equal("Active", await check.StockReservations.Where(r => r.OrderId == next.Id).Select(r => r.Status).SingleAsync());
        Assert.Equal(0, await InventoryScenario.Service(check).ExpireAsync(default));
        await s.MakeExpired(next.Id);
        Assert.Equal(1, await InventoryScenario.Service(check).ExpireAsync(default));
        Assert.Equal(0, await InventoryScenario.Service(check).ExpireAsync(default));
    }

    [PostgresFact]
    public async Task CancellationAndPaymentRaceHasOneTerminalReservationAndNoDoubleConsumption()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1));
        var order = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA, request)).Body);
        await using var a = ShoppingScenario.Open(); await using var b = ShoppingScenario.Open();
        var results = await Task.WhenAll(InventoryScenario.Service(a).CancelAsync(s.Shopping.UserA, order.Id, default),
            InventoryScenario.Service(b).ConfirmPaymentAsync(order.Id, Guid.NewGuid(), order.GoodsTotal, "PLN", default));
        await using var check = ShoppingScenario.Open();
        var final = Assert.IsType<OrderResponse>((await InventoryScenario.Service(check).GetAsync(s.Shopping.UserA, order.Id, default)).Body);
        Assert.Contains(final.Status, new[] { "Paid", "PaymentReview" });
        Assert.Equal(final.Status == "Paid" ? "Consumed" : "Released", final.ReservationStatus);
        Assert.Equal(final.Status == "Paid" ? 2 : 3, await check.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).Select(v => v.QuantityOnHand).SingleAsync());
        Assert.Equal(final.Status == "Paid" ? 1 : 0, await check.StockMovements.CountAsync());
        Assert.All(results, r => Assert.Contains(r.Status, new[] { 200, 409 }));
    }

    [PostgresFact]
    public async Task CorrectionsSqlAndSeederCannotDestroyReservedStockOrHistory()
    {
        await using var s = await InventoryScenario.Create();
        var order = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA,
            await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 2)))).Body);
        await using var db = ShoppingScenario.Open();
        var stock = await db.VariantStocks.AsNoTracking().SingleAsync(v => v.VariantId == s.Shopping.Cap);
        var e = await Assert.ThrowsAsync<CatalogValidation>(() => new VariantStockService(db, new(db, TimeProvider.System), TimeProvider.System)
            .SetAsync(s.Shopping.Cap, new(1, stock.Version, "Korekta"), s.Shopping.UserA, Guid.NewGuid(), default));
        Assert.Equal("stock_below_reservations", e.Code);
        var sql = await Assert.ThrowsAsync<PostgresException>(() => db.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap)
            .ExecuteUpdateAsync(v => v.SetProperty(x => x.QuantityOnHand, 1)));
        Assert.Equal("23514", sql.SqlState);
        await Assert.ThrowsAsync<InventorySeedBlockedException>(() => new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(3, 3), new(1, 1)), default));
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(3, await db.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).Select(v => v.QuantityOnHand).SingleAsync());
        var cancelled = await InventoryScenario.Service(db).CancelAsync(s.Shopping.UserA, order.Id, default);
        Assert.Equal("Cancelled", Assert.IsType<OrderResponse>(cancelled.Body).Status);
        Assert.Equal("Cancelled", Assert.IsType<OrderResponse>((await InventoryScenario.Service(db).CancelAsync(s.Shopping.UserA, order.Id, default)).Body).Status);
        Assert.Equal(404, (await InventoryScenario.Service(db).GetAsync(s.Shopping.UserB, order.Id, default)).Status);
        Assert.Equal(404, (await InventoryScenario.Service(db).CancelAsync(s.Shopping.UserB, order.Id, default)).Status);
        var history = await Assert.ThrowsAsync<PostgresException>(() => db.OrderItems.Where(i => i.OrderId == order.Id).ExecuteDeleteAsync());
        Assert.Equal("P0001", history.SqlState);
    }

    [PostgresFact]
    public async Task TwoHostedWorkersRecoverExistingExpiredOrders()
    {
        await using var s = await InventoryScenario.Create();
        var order = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA,
            await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1)))).Body);
        await s.MakeExpired(order.Id);
        var workerOptions = Options.Create(new InventoryOptions { ExpiryIntervalSeconds = 1 });
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => ShoppingScenario.Open());
        registrations.AddSingleton<TimeProvider>(TimeProvider.System);
        registrations.AddSingleton<IOptions<InventoryOptions>>(workerOptions);
        registrations.AddScoped<InventoryOrderService>();
        await using var provider = registrations.BuildServiceProvider();
        using var first = new ReservationExpiryWorker(provider.GetRequiredService<IServiceScopeFactory>(), workerOptions, NullLogger<ReservationExpiryWorker>.Instance);
        using var second = new ReservationExpiryWorker(provider.GetRequiredService<IServiceScopeFactory>(), workerOptions, NullLogger<ReservationExpiryWorker>.Instance);
        await first.StartAsync(default); await second.StartAsync(default);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var db = ShoppingScenario.Open();
                if (await db.Orders.Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync() == "Expired") break;
                await Task.Delay(50);
            }
            await using var check = ShoppingScenario.Open();
            Assert.Equal("Expired", await check.Orders.Select(o => o.Status).SingleAsync());
            Assert.Equal(0, await InventoryScenario.Service(check).ExpireAsync(default));
            Assert.Empty(await check.StockMovements.ToArrayAsync());
        }
        finally { await first.StopAsync(default); await second.StopAsync(default); }
    }

    [PostgresFact]
    public async Task UnknownZeroAndHiddenStockBlockCheckoutWithoutChangingCart()
    {
        await using var s = await InventoryScenario.Create();
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1));
        await using var db = ShoppingScenario.Open();
        await db.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).ExecuteUpdateAsync(v => v.SetProperty(x => x.QuantityOnHand, 0));
        Assert.Equal("out_of_stock", Assert.IsType<ShoppingError>((await s.CreateOrder(s.Shopping.UserA, request)).Body).Code);
        await db.VariantStocks.Where(v => v.VariantId == s.Shopping.Cap).ExecuteDeleteAsync();
        Assert.Equal("stock_unknown", Assert.IsType<ShoppingError>((await s.CreateOrder(s.Shopping.UserA, request)).Body).Code);
        var productId = await db.ProductVariants.Where(v => v.Id == s.Shopping.Cap).Select(v => v.ProductId).SingleAsync();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, E_commerce_backend.Entities.CatalogStatus.Hidden));
        Assert.Equal("unavailable", Assert.IsType<ShoppingError>((await s.CreateOrder(s.Shopping.UserA, request)).Body).Code);
        Assert.Equal(0, await db.Orders.CountAsync()); Assert.Equal(1, await db.CartItems.CountAsync());
    }

    [PostgresFact]
    public async Task ZeroPriceRemainsValidAndSettlementKeepsImmutableSnapshotAfterPriceChange()
    {
        await using var s = await InventoryScenario.Create();
        await using (var db = ShoppingScenario.Open())
        {
            var price = await E_commerce_backend.Services.Pricing.CurrentVariantPrices.Query(db, [s.Shopping.Cap], DateTime.UtcNow).SingleAsync();
            var result = await new E_commerce_backend.Services.Pricing.VariantPriceService(db, TimeProvider.System)
                .ChangeAsync(new(s.Shopping.Cap, 0, "PLN", "Darmowy towar", price.Id, Guid.NewGuid(), s.Shopping.UserA));
            Assert.Equal(E_commerce_backend.Services.Pricing.PriceChangeStatus.Applied, result.Status);
        }
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 1));
        Assert.Equal(0, request.Items![0].UnitPrice);
        var order = Assert.IsType<OrderResponse>((await s.CreateOrder(s.Shopping.UserA, request)).Body);
        await using var check = ShoppingScenario.Open();
        var latest = await E_commerce_backend.Services.Pricing.CurrentVariantPrices.Query(check, [s.Shopping.Cap], DateTime.UtcNow).SingleAsync();
        var changed = await new E_commerce_backend.Services.Pricing.VariantPriceService(check, TimeProvider.System)
            .ChangeAsync(new(s.Shopping.Cap, 50, "PLN", "Nowa cena", latest.Id, Guid.NewGuid(), s.Shopping.UserA));
        Assert.Equal(E_commerce_backend.Services.Pricing.PriceChangeStatus.Applied, changed.Status);
        var paid = Assert.IsType<OrderResponse>((await InventoryScenario.Service(check).ConfirmPaymentAsync(order.Id, Guid.NewGuid(), 0, "PLN", default)).Body);
        Assert.Equal("Paid", paid.Status); Assert.Equal(0, paid.GoodsTotal); Assert.Equal(0, Assert.Single(paid.Items).UnitPrice);
    }
}

internal sealed class InventoryScenario(ShoppingScenario shopping) : IAsyncDisposable
{
    public ShoppingScenario Shopping { get; } = shopping;
    public static async Task<InventoryScenario> Create() => new(await ShoppingScenario.Create());
    public static InventoryOrderService Service(AppDbContext db) => new(db, TimeProvider.System, Options.Create(new InventoryOptions()));
    public async Task<OrderCreateRequest> Request(ShoppingOwner owner, params (Guid Variant, int Quantity)[] items)
    {
        foreach (var item in items)
        {
            var current = await Shopping.Cart(owner);
            Assert.Equal(200, (await Shopping.SetCart(owner, item.Variant, item.Quantity, ShoppingPostgresTests.Stamp(current))).Status);
        }
        var cart = await Shopping.Cart(owner);
        return new(cart.ScopeId!.Value, cart.Version, cart.Items.Select(i => new OrderPriceAcceptance(i.VariantId, i.Quantity, i.Variant!.PriceChangeId, i.Variant.Price.Amount)).ToArray());
    }
    public async Task<ShoppingResult> CreateOrder(Guid user, OrderCreateRequest request, Guid? key = null)
    {
        await using var db = ShoppingScenario.Open();
        return await Service(db).CreateAsync(user, key ?? Guid.NewGuid(), request, default);
    }
    public async Task MakeExpired(Guid orderId)
    {
        // Privileged fixture only: simulate time passing without a minute-long sleep.
        await using var db = ShoppingScenario.Open();
        await using var tx = await db.Database.BeginTransactionAsync();
        await E_commerce_backend.Services.Pricing.CatalogWriteLock.AcquireAsync(db, default);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE app.stock_reservations DISABLE TRIGGER inventory_identity");
        await db.StockReservations.Where(r => r.OrderId == orderId).ExecuteUpdateAsync(r =>
            r.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-20)).SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE app.stock_reservations ENABLE TRIGGER inventory_identity");
        await tx.CommitAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await using var db = ShoppingScenario.Open();
        // Explicit isolated-fixture cleanup, never part of the application seeder.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE app.inventory_payment_receipts, app.stock_movements, app.stock_reservation_items, app.stock_reservations, app.order_items, app.orders RESTRICT");
        await Shopping.DisposeAsync();
    }
}
