using System.Diagnostics;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connection = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE") ?? throw new InvalidOperationException("Set isolated local ECOMMERCE_TEST_DATABASE.");
var address = new NpgsqlConnectionStringBuilder(connection);
if (address.Host is not ("127.0.0.1" or "localhost") || address.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true)
    throw new InvalidOperationException("Only isolated local ecomm_test_* databases are accepted; seeder resets the catalog.");
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection,
    options => options.MigrationsHistoryTable("__EcommerceMigrationsHistory", "public")).Options);
await db.Database.MigrateAsync();
await new DemoCatalogSeeder(db).SeedAsync(new(new(80, 80), new(5000, 5000), new(3, 3)), default);
var actor = Guid.NewGuid();
foreach (var p in await db.Promotions.AsNoTracking().Where(p => p.Status == "Active").ToArrayAsync())
{
    var ended = await new PromotionService(db, TimeProvider.System).MutateAsync("end", p.Id, null, new() { ExpectedVersion = p.Version }, Guid.NewGuid(), actor, default);
    if (ended.Status != 200) throw new InvalidOperationException(JsonSerializer.Serialize(ended));
}
var samples = new List<object>();
foreach (var discount in new[] { 90m, 95m, 99m })
{
    var service = new PromotionService(db, TimeProvider.System);
    var draft = await service.MutateAsync("create", Guid.Empty, new() { Scope = "catalog", Name = "Performance", DiscountType = "percentage", DiscountValue = discount }, null, Guid.NewGuid(), actor, default);
    if (draft.Status != 201) throw new InvalidOperationException(JsonSerializer.Serialize(draft));
    var p = ((PromotionCommandResponse)draft.Body).Promotion;
    var sw = Stopwatch.StartNew();
    var previewResult = await service.PreviewAsync(p.Id, 1, 20, default);
    var previewMs = sw.Elapsed.TotalMilliseconds;
    var preview = (PromotionPreviewResponse)previewResult.Body;
    if (!preview.CanActivate) throw new InvalidOperationException(JsonSerializer.Serialize(preview));
    var body = new PromotionActivationRequest { ExpectedVersion = p.Version, ExpectedScopeToken = preview.ScopeToken };
    sw.Restart();
    var activationTask = service.MutateAsync("activate", p.Id, null, body, Guid.NewGuid(), actor, default);
    await Task.Delay(20);
    var waitTask = WaitForWriter();
    var activated = await activationTask;
    var activationMs = sw.Elapsed.TotalMilliseconds;
    var writerWaitMs = await waitTask;
    if (activated.Status != 200) throw new InvalidOperationException(JsonSerializer.Serialize(activated));
    p = ((PromotionCommandResponse)activated.Body).Promotion;
    sw.Restart();
    await using (var workerDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options))
    {
        await using var tx = await workerDb.Database.BeginTransactionAsync();
        await CatalogWriteLock.AcquireAsync(workerDb, default);
        await new DynamicPromotionEngine(workerDb, TimeProvider.System).ReconcileAsync(default);
        await tx.CommitAsync();
    }
    var noChangeReconcileMs = sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    var ended = await service.MutateAsync("end", p.Id, null, new() { ExpectedVersion = p.Version }, Guid.NewGuid(), actor, default);
    var endMs = sw.Elapsed.TotalMilliseconds;
    if (ended.Status != 200) throw new InvalidOperationException(JsonSerializer.Serialize(ended));
    samples.Add(new { discount, preview.TotalVariants, preview.EligibleVariants, preview.ConflictedVariants, p.ActiveVariants,
        previewMs, activationMs, writerWaitMs, noChangeReconcileMs, endMs, activationBodyBytes = JsonSerializer.SerializeToUtf8Bytes(body).Length });
}
Console.WriteLine(JsonSerializer.Serialize(new { products = await db.Products.CountAsync(), variants = await db.ProductVariants.CountAsync(), samples }, new JsonSerializerOptions { WriteIndented = true }));

async Task<double> WaitForWriter()
{
    await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    await using var tx = await other.Database.BeginTransactionAsync();
    var sw = Stopwatch.StartNew();
    await CatalogWriteLock.AcquireAsync(other, default);
    return sw.Elapsed.TotalMilliseconds;
}
