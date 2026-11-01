using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog.Management;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("ECOMMERCE_PERFORMANCE_DATABASE") ?? throw new Exception("Set ECOMMERCE_PERFORMANCE_DATABASE");
var address = new NpgsqlConnectionStringBuilder(connection);
if (address.Host is not ("localhost" or "127.0.0.1") || address.Database?.StartsWith("ecomm_perf", StringComparison.Ordinal) != true)
    throw new Exception("Only an isolated local ecomm_perf* database is allowed");
var counter = new Counter();
AppDbContext Open(bool measured = true) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(measured ? [counter] : []).Options);
await using var setup = Open(false);
if (args.Contains("--prepare"))
{
    await new DemoCatalogSeeder(setup).SeedAsync(new(new(60, 60), new(5000, 5000), new(3, 3)), default);
    var heavy = new Product { Name = "Pomiar 100 wariantów", Slug = "offer-audit-heavy" };
    var key = new AttributeKey { Code = "offer-audit-option", DisplayName = "Konfiguracja" };
    setup.AddRange(heavy, key);
    setup.ProductCategories.Add(new() { ProductId = heavy.Id, CategoryId = await setup.Categories.Select(c => c.Id).FirstAsync() });
    for (var i = 0; i < 100; i++)
    {
        var v = new ProductVariant { ProductId = heavy.Id, SKU = $"OFFER-AUDIT-{i:D3}" };
        var pair = new AttributePair { AttributeKeyId = key.Id, Value = i.ToString() };
        setup.AddRange(v, pair, new VariantAttributePair { VariantId = v.Id, AttributePairId = pair.Id },
            new VariantStock { VariantId = v.Id, QuantityOnHand = 10 },
            new VariantPriceChange { VariantId = v.Id, BaseAmount = 50, SellingAmount = 50, EffectiveAt = DateTime.UtcNow, Reason = "Synthetic audit" });
    }
    await setup.SaveChangesAsync(); setup.ChangeTracker.Clear();
    await using var tx = await setup.Database.BeginTransactionAsync();
    await CatalogWriteLock.AcquireAsync(setup, default);
    await setup.ProductVariants.Where(v => v.ProductId == heavy.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Published));
    await setup.Products.Where(p => p.Id == heavy.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Published));
    await tx.CommitAsync();
    await setup.Database.ExecuteSqlRawAsync("ANALYZE app.products, app.product_variants, app.variant_stock, app.variant_attribute_pairs, app.variant_price_changes");
}
var heavyId = await setup.Products.Where(p => p.Slug == "offer-audit-heavy").Select(p => p.Id).SingleAsync();
var variantId = await setup.ProductVariants.Where(v => v.ProductId == heavyId).OrderBy(v => v.SKU).Select(v => v.Id).FirstAsync();
Console.WriteLine(JsonSerializer.Serialize(new { products = await setup.Products.CountAsync(), variants = await setup.ProductVariants.CountAsync(), prices = await setup.VariantPriceChanges.CountAsync(), samples = 30, warmup = 5 }));
var actor = Guid.NewGuid();
async Task Measure(string name, Func<AppDbContext, Task> action)
{
    var samples = new List<(double Ms, int Sql, double LockMs)>();
    for (var i = 0; i < 35; i++)
    {
        await using var db = Open(); counter.Count = 0; counter.LockMs = 0;
        var sw = Stopwatch.StartNew(); await action(db); sw.Stop();
        if (i >= 5) samples.Add((sw.Elapsed.TotalMilliseconds, counter.Count, counter.LockMs));
    }
    var ordered = samples.Select(s => s.Ms).Order().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { name, samples = samples.Count, p50Ms = Math.Round(ordered[14], 2),
        p95Ms = Math.Round(ordered[28], 2), sqlMin = samples.Min(s => s.Sql), sqlMax = samples.Max(s => s.Sql), lockP95Ms = Math.Round(samples.Select(s => s.LockMs).Order().ElementAt(28), 2) }));
}
await Measure("admin-search-page20", async db =>
{
    var reader = new CatalogManagementReader(db, TimeProvider.System);
    var result = await reader.SnapshotAsync(() => reader.ProductsAsync(new(PageSize: 20), default), default);
    if (result.Status != 200) throw new Exception("Admin search failed");
});
await Measure("variants100-page20", async db =>
{
    var reader = new CatalogManagementReader(db, TimeProvider.System);
    var result = await reader.SnapshotAsync(() => reader.VariantsAsync(heavyId, 1, 20, default), default);
    if (((AdminPage<AdminVariantSummary>)result.Body!).Items.Count != 20) throw new Exception("Variant page failed");
});
await Measure("readiness100-page20", async db =>
{
    var reader = new CatalogManagementReader(db, TimeProvider.System);
    var result = await reader.SnapshotAsync(() => reader.ReadinessAsync(heavyId, false, 1, 20, default), default);
    if (!((PublicationReadiness)result.Body!).CanPublish) throw new Exception("Readiness failed");
});
await Measure("public-catalog-page20", async db =>
{
    var result = await new ProductsController(db).GetCatalog(1, 20, null, "price-asc", null, null, default);
    if (result is not OkObjectResult) throw new Exception("Public catalog failed");
});
await Measure("public-search-page20", async db =>
{
    var result = await new ProductsController(db).Search(new ProductSearchRequest { PageSize = 20 }, default);
    if (result is not OkObjectResult) throw new Exception("Public search failed");
});
await Measure("edit-product100", async db =>
{
    var p = await db.Products.AsNoTracking().SingleAsync(p => p.Id == heavyId);
    var service = new ProductManagementService(db, new(db, TimeProvider.System), TimeProvider.System);
    var result = await service.EditAsync(heavyId, new(p.Name, p.Slug, p.Version), actor, Guid.NewGuid(), default);
    if (result.Status != 200) throw new Exception(JsonSerializer.Serialize(result));
});
await Measure("stock-correction", async db =>
{
    var stock = await db.VariantStocks.AsNoTracking().SingleAsync(s => s.VariantId == variantId);
    var result = await new VariantStockService(db, new(db, TimeProvider.System), TimeProvider.System).SetAsync(
        variantId, new(stock.QuantityOnHand, stock.Version, "Synthetic audit"), actor, Guid.NewGuid(), default);
    if (result.Status != 200) throw new Exception(JsonSerializer.Serialize(result));
});
// Record actual catalog lock acquisition under concurrent metadata commands.
var ids = await setup.Products.Where(p => p.Id != heavyId).Select(p => p.Id).Take(8).ToArrayAsync();
var parallel = await Task.WhenAll(ids.Select(async id =>
{
    var local = new Counter();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(local).Options);
    var p = await db.Products.AsNoTracking().SingleAsync(p => p.Id == id);
    var result = await new ProductManagementService(db, new(db, TimeProvider.System), TimeProvider.System)
        .EditAsync(id, new(p.Name, p.Slug, p.Version), actor, Guid.NewGuid(), default);
    if (result.Status != 200) throw new Exception("Parallel edit failed");
    return local.LockMs;
}));
Console.WriteLine(JsonSerializer.Serialize(new { name = "8-parallel-edits", lockMinMs = Math.Round(parallel.Min(), 2), lockMaxMs = Math.Round(parallel.Max(), 2) }));

sealed class Counter : DbCommandInterceptor
{
    public int Count;
    public double LockMs;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    { Count++; return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { Count++; return ValueTask.FromResult(result); }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
    { if (command.CommandText.Contains("pg_advisory_xact_lock(20261031", StringComparison.Ordinal)) LockMs += data.Duration.TotalMilliseconds; return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken ct = default)
    { Count++; return ValueTask.FromResult(result); }
}
