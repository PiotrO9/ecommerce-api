using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("ECOMMERCE_PERFORMANCE_DATABASE") ?? throw new Exception("Set ECOMMERCE_PERFORMANCE_DATABASE");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Host is not ("localhost" or "127.0.0.1") || cs.Database?.StartsWith("ecomm_perf", StringComparison.Ordinal) != true) throw new Exception("Isolated local ecomm_perf* database required");
var counter = new Counter();
AppDbContext Open(bool instrument = true) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(instrument ? [counter] : []).Options);
await using var setup = Open(false);
if (await setup.Collections.AnyAsync(c => c.Slug.StartsWith("perf-collection-audit-"))) throw new Exception("Audit already prepared; restore disposable benchmark database");
var products = await setup.Products.Where(p => p.Slug.StartsWith("perf-produkt-")).OrderBy(p => p.Slug).Take(500).Select(p => p.Id).ToArrayAsync();
if (products.Length != 500) throw new Exception("Prepare 5000-product benchmark first");
var variants = await setup.ProductVariants.Where(v => products.Contains(v.ProductId)).Select(v => v.Id).ToArrayAsync();
var prices = await setup.VariantPriceChanges.AsNoTracking().Where(p => variants.Contains(p.VariantId) && p.Currency == "PLN").ToArrayAsync();
var now = DateTime.UtcNow;
foreach (var group in prices.GroupBy(p => p.VariantId))
{
    var current = group.OrderByDescending(p => p.EffectiveAt).ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).First();
    setup.VariantPriceChanges.Add(new() { VariantId = group.Key, BaseAmount = current.BaseAmount, SellingAmount = current.BaseAmount,
        EffectiveAt = now.AddHours(-1), CreatedAt = now, Reason = "Synthetic collection benchmark regular price" });
    setup.VariantOfferEvents.Add(new() { VariantId = group.Key, OccurredAt = now.AddDays(-120), Visible = true, CompleteFromBirth = true, Source = "demo" });
}
await setup.SaveChangesAsync();
var actor = Guid.NewGuid();
foreach (var (count, batch) in new[] { (10, 0), (100, 1), (500, 2) })
{
    await using var db = Open(); var service = new CollectionService(db, TimeProvider.System);
    var created = await service.MutateAsync("create", Guid.Empty, new() { DisplayName = $"Audit {count}", Slug = $"perf-collection-audit-{count}" }, null, Guid.NewGuid(), actor, default);
    var c = ((CollectionCommandResponse)created.Body).Collection;
    var linked = await service.MutateAsync("members", c.Id, null, new() { ExpectedVersion = c.Version, ProductIds = products.Take(count).ToList() }, Guid.NewGuid(), actor, default);
    c = ((CollectionCommandResponse)linked.Body).Collection;
    var publish = await service.MutateAsync("update", c.Id, new() { DisplayName = c.DisplayName, Slug = c.Slug, Status = "Published", ExpectedVersion = c.Version }, null, Guid.NewGuid(), actor, default);
    if (publish.Status != 200) throw new Exception(JsonSerializer.Serialize(publish));
    var samples = new Dictionary<string, List<(double Ms, int Sql)>>();
    async Task<PromotionResult> Measure(string name, Func<Task<PromotionResult>> action, bool record)
    {
        counter.Count = 0; var sw = Stopwatch.StartNew(); var result = await action(); sw.Stop();
        if (result.Status != 200) throw new Exception(JsonSerializer.Serialize(result));
        if (record) { if (!samples.TryGetValue(name, out var rows)) samples[name] = rows = []; rows.Add((sw.Elapsed.TotalMilliseconds, counter.Count)); }
        return result;
    }
    double lockWaitMs = 0;
    for (var i = 0; i < 8; i++)
    {
        var promo = new PromotionService(db, TimeProvider.System);
        var result = await promo.MutateAsync("create", Guid.Empty, new() { Scope = "collection", CollectionId = c.Id, Name = "Audit", DiscountType = "percentage", DiscountValue = 50 + batch * 10 + i }, null, Guid.NewGuid(), actor, default);
        var p = ((PromotionCommandResponse)result.Body).Promotion;
        var preview = (PromotionPreviewResponse)(await Measure("preview", () => promo.PreviewAsync(p.Id, 1, 20, default), i >= 3)).Body;
        if (!preview.CanActivate) throw new Exception(JsonSerializer.Serialize(preview));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        counter.OnLock = () => signal.TrySetResult();
        var activation = Measure("activate", () => promo.MutateAsync("activate", p.Id, null, new PromotionActivationRequest {
            ExpectedVersion = p.Version, ExpectedCollectionVersion = preview.CollectionVersion, Variants = preview.ExpectedPrices.ToList() }, Guid.NewGuid(), actor, default), i >= 3);
        if (i == 7)
        {
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var waiter = Open(false);
            await using var tx = await waiter.Database.BeginTransactionAsync();
            var sw = Stopwatch.StartNew(); await CatalogWriteLock.AcquireAsync(waiter, default); sw.Stop(); lockWaitMs = sw.Elapsed.TotalMilliseconds;
            await tx.CommitAsync();
        }
        await activation; counter.OnLock = null;
        await Measure("end", () => promo.MutateAsync("end", p.Id, null, new() { ExpectedVersion = p.Version + 1 }, Guid.NewGuid(), actor, default), i >= 3);
    }
    foreach (var (name, rows) in samples)
    {
        var times = rows.Select(r => r.Ms).Order().ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { products = count, variants = count * 3, name, samples = rows.Count,
            p50Ms = Math.Round(times[2], 1), p95Ms = Math.Round(times[^1], 1), minSql = rows.Min(r => r.Sql), maxSql = rows.Max(r => r.Sql) }));
    }
    var controller = new ProductsController(db); counter.Count = 0;
    await controller.Search(new() { CollectionId = c.Id, MinPrice = 100, MaxPrice = 300, Sort = "price-asc" }, default);
    Console.WriteLine(JsonSerializer.Serialize(new { products = count, collectionSearchSql = counter.Count, lockWaitMs = Math.Round(lockWaitMs, 1) }));
}

sealed class Counter : DbCommandInterceptor
{
    public int Count;
    public Action? OnLock;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Count++; return ValueTask.FromResult(r); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Count++; return ValueTask.FromResult(r); }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData e, int r, CancellationToken ct = default) { if (c.CommandText.Contains("pg_advisory_xact_lock(20261031, 3)")) OnLock?.Invoke(); return ValueTask.FromResult(r); }
}
