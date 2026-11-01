using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

// Mutates a synthetic benchmark product only. Never use on a real shop database.
var connection = Environment.GetEnvironmentVariable("ECOMMERCE_PERFORMANCE_DATABASE") ?? throw new Exception("Set ECOMMERCE_PERFORMANCE_DATABASE");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Host is not ("127.0.0.1" or "localhost") || cs.Database?.StartsWith("ecomm_perf", StringComparison.Ordinal) != true)
    throw new Exception("Requires isolated local ecomm_perf* database");
var counter = new CommandCounter();
AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(counter).Options);
await using var db = Open();
var product = await db.Products.SingleAsync(p => p.Slug == "demo-czapka");
if (args.Contains("--read-count"))
{
    var controller = new ProductsController(db);
    counter.Count = 0;
    await controller.GetCatalog(1, 20, null, "price-asc", 100, 300, default);
    Console.WriteLine($"catalog-price SQL={counter.Count}");
    counter.Count = 0;
    await controller.Search(new() { MinPrice = 100, MaxPrice = 300,
        Attributes = [new() { Code = "perf-kolor", Values = ["czerwony"] }, new() { Code = "perf-rozmiar", Values = ["S"] }],
        Sort = "price-asc", Page = 1, PageSize = 20 }, default);
    Console.WriteLine($"search-2-price SQL={counter.Count}");
    return;
}
if (args.Contains("--activate-existing"))
{
    var service = new PromotionService(db, TimeProvider.System);
    var actorId = Guid.NewGuid();
    var created = await service.MutateAsync("create", Guid.Empty, new() { ProductId = product.Id,
        Name = "Aktywna promocja do pomiaru odczytów", DiscountType = "percentage", DiscountValue = 40 }, null, Guid.NewGuid(), actorId, default);
    var promotion = ((PromotionCommandResponse)created.Body).Promotion;
    var preview = (PromotionPreviewResponse)(await service.PreviewAsync(promotion.Id, 1, 20, default)).Body;
    var result = await service.MutateAsync("activate", promotion.Id, null, new PromotionActivationRequest {
        ExpectedVersion = promotion.Version, Variants = preview.ExpectedPrices.ToList() }, Guid.NewGuid(), actorId, default);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return;
}
if (await db.ProductVariants.AnyAsync(v => v.SKU.StartsWith("PERF-PROMOTION-")))
    throw new Exception("Audit already prepared; restore the disposable database before another run");
var now = DateTime.UtcNow;
var attribute = new AttributeKey { Code = "perf-promotion-number", DisplayName = "Numer wariantu pomiarowego" };
db.AttributeKeys.Add(attribute);
for (var i = 0; i < 50; i++)
{
    var variant = new ProductVariant { ProductId = product.Id, SKU = $"PERF-PROMOTION-{i:000}", Status = CatalogStatus.Published };
    db.ProductVariants.Add(variant);
    var pair = new AttributePair { AttributeKeyId = attribute.Id, Value = i.ToString() };
    db.AttributePairs.Add(pair);
    db.VariantAttributePairs.Add(new() { VariantId = variant.Id, AttributePairId = pair.Id });
    db.VariantPriceChanges.Add(new() { VariantId = variant.Id, BaseAmount = 100, SellingAmount = 100,
        EffectiveAt = now.AddDays(-40), CreatedAt = now.AddDays(-40), Reason = "Synthetic promotion benchmark" });
}
await db.SaveChangesAsync();
var ids = await db.ProductVariants.Where(v => v.ProductId == product.Id).Select(v => v.Id).ToArrayAsync();
foreach (var id in ids)
    db.VariantOfferEvents.Add(new() { VariantId = id, OccurredAt = now.AddDays(-40), Visible = true, CompleteFromBirth = true, Source = "demo" });
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var samples = new Dictionary<string, List<(double Ms, int Sql)>>();
async Task<PromotionResult> Measure(string label, Func<Task<PromotionResult>> action, bool record)
{
    counter.Count = 0;
    var sw = Stopwatch.StartNew();
    var result = await action();
    sw.Stop();
    if (result.Status is not (200 or 201)) throw new Exception(JsonSerializer.Serialize(result));
    if (record)
    {
        if (!samples.TryGetValue(label, out var rows)) samples[label] = rows = [];
        rows.Add((sw.Elapsed.TotalMilliseconds, counter.Count));
    }
    return result;
}
var actor = Guid.NewGuid();
for (var i = 0; i < 13; i++)
{
    await using var session = Open();
    var service = new PromotionService(session, TimeProvider.System);
    var create = await service.MutateAsync("create", Guid.Empty,
        new() { ProductId = product.Id, Name = "Benchmark", DiscountType = "percentage", DiscountValue = 10 + i }, null, Guid.NewGuid(), actor, default);
    var p = ((PromotionCommandResponse)create.Body).Promotion;
    var preview = (PromotionPreviewResponse)(await Measure("preview", () => service.PreviewAsync(p.Id, 1, 20, default), i >= 3)).Body;
    if (!preview.CanActivate) throw new Exception(JsonSerializer.Serialize(preview));
    await Measure("activate", () => service.MutateAsync("activate", p.Id, null,
        new PromotionActivationRequest { ExpectedVersion = p.Version, Variants = preview.ExpectedPrices.ToList() }, Guid.NewGuid(), actor, default), i >= 3);
    await Measure("end", () => service.MutateAsync("end", p.Id, null,
        new() { ExpectedVersion = p.Version + 1 }, Guid.NewGuid(), actor, default), i >= 3);
}
Console.WriteLine(JsonSerializer.Serialize(new { products = await db.Products.CountAsync(), variantsInPromotion = ids.Length }));
foreach (var (name, rows) in samples)
{
    var times = rows.Select(r => r.Ms).Order().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { name, samples = rows.Count,
        p50Ms = Math.Round((times[4] + times[5]) / 2, 1), p95Ms = Math.Round(times[9], 1),
        minSql = rows.Min(r => r.Sql), maxSql = rows.Max(r => r.Sql) }));
}

sealed class CommandCounter : DbCommandInterceptor
{
    public int Count;
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Count++; return ValueTask.FromResult(r); }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<int> r, CancellationToken ct = default) { Count++; return ValueTask.FromResult(r); }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<object> r, CancellationToken ct = default) { Count++; return ValueTask.FromResult(r); }
}
