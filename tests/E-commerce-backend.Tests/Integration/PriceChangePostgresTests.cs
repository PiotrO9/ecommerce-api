using E_commerce_backend.Data;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Controllers;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class PriceChangePostgresTests
{
    [PostgresFact]
    public async Task FirstPriceChangesAndReplaysKeepOriginalReceiptAndHistory()
    {
        var variantId = await PrepareAsync(unpriced: true);
        var firstCommand = Command(variantId) with { Currency = " pln ", Reason = "  Pierwsza cena  " };
        var first = await ChangeAsync(firstCommand);
        Assert.Equal(PriceChangeStatus.Applied, first.Status);
        Assert.Equal("PLN", first.Price!.Currency);
        Assert.Equal("Pierwsza cena", first.Price.Reason);
        Assert.Equal(first.Price.BaseAmount, first.Price.SellingAmount);
        Assert.Null(first.Price.PreviousPriceChangeId);

        var second = await ChangeAsync(firstCommand with
        {
            BaseAmount = 0m, OperationKey = Guid.NewGuid(), ExpectedPriceChangeId = first.Price.Id
        });
        Assert.Equal(PriceChangeStatus.Applied, second.Status);
        Assert.Equal(0m, second.Price!.SellingAmount);
        Assert.Equal(first.Price.Id, second.Price.PreviousPriceChangeId);
        Assert.True(second.Price.EffectiveAt > first.Price.EffectiveAt);

        // Replay precedes version checking: even after another change it returns the original result.
        var replay = await ChangeAsync(firstCommand);
        Assert.Equal(PriceChangeStatus.Replayed, replay.Status);
        Assert.Equal(first.Price, replay.Price);
        foreach (var mismatch in new[]
        {
            firstCommand with { BaseAmount = 20m }, firstCommand with { Reason = "Inny powód" },
            firstCommand with { ExpectedPriceChangeId = first.Price.Id },
            firstCommand with { VariantId = Guid.NewGuid() }
        })
        {
            Assert.Equal(PriceChangeStatus.OperationKeyConflict, (await ChangeAsync(mismatch)).Status);
        }
        Assert.Equal(PriceChangeStatus.VersionConflict,
            (await ChangeAsync(firstCommand with { OperationKey = Guid.NewGuid() })).Status);

        await using var database = Open();
        Assert.Equal(2, await database.VariantPriceChanges.CountAsync(p => p.VariantId == variantId));
        var latest = await database.VariantPriceChanges.Where(p => p.VariantId == variantId)
            .OrderByDescending(p => p.EffectiveAt).Select(p => p.Id).FirstAsync();
        Assert.Equal(second.Price.Id, latest);

        var cap = await database.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        var importedPrice = await database.VariantPriceChanges.SingleAsync(p => p.VariantId == cap.Id);
        var updated = await ChangeAsync(Command(cap.Id) with
            { BaseAmount = 99m, ExpectedPriceChangeId = importedPrice.Id });
        Assert.Equal(PriceChangeStatus.Applied, updated.Status);
        Assert.Equal(importedPrice.Id, updated.Price!.PreviousPriceChangeId);
        var details = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(
            await new ProductsController(database).Get(cap.ProductId, null, CancellationToken.None)).Value);
        Assert.Equal(99m, Assert.Single(Assert.Single(details.Variants).CurrentPrices).Amount);
    }

    [PostgresFact]
    public async Task ConcurrentWritersAndRetriesSerializeIncludingFirstPrice()
    {
        var variantId = await PrepareAsync(unpriced: true);
        var first = Command(variantId);
        var firstRace = await RaceAsync(first, first with { OperationKey = Guid.NewGuid(), BaseAmount = 30m });
        Assert.Single(firstRace, result => result.Status == PriceChangeStatus.Applied);
        Assert.Single(firstRace, result => result.Status == PriceChangeStatus.VersionConflict);
        var current = firstRace.Single(result => result.Status == PriceChangeStatus.Applied).Price!;

        var next = first with { OperationKey = Guid.NewGuid(), ExpectedPriceChangeId = current.Id };
        var retries = await RaceAsync(next, next);
        Assert.Single(retries, result => result.Status == PriceChangeStatus.Applied);
        Assert.Single(retries, result => result.Status == PriceChangeStatus.Replayed);
        Assert.Equal(retries[0].Price, retries[1].Price);

        await using var database = Open();
        var otherVariant = await AddUnpricedVariantAsync(database);
        var crossVariantKey = Guid.NewGuid();
        var crossRace = await RaceAsync(
            next with { OperationKey = crossVariantKey, ExpectedPriceChangeId = retries[0].Price!.Id },
            next with { VariantId = otherVariant, OperationKey = crossVariantKey, ExpectedPriceChangeId = null });
        Assert.Single(crossRace, result => result.Status == PriceChangeStatus.Applied);
        Assert.Single(crossRace, result => result.Status == PriceChangeStatus.OperationKeyConflict);
        Assert.Equal(1, await database.VariantPriceChanges.CountAsync(p =>
            p.OperationKey == crossVariantKey && p.ChangedByUserId == first.ActorUserId));
    }

    [PostgresFact]
    public async Task ConflictsPreserveDiscountsFuturePricesAndTimestampOrdering()
    {
        var capId = await PrepareAsync();
        await using var database = Open();
        var current = await database.VariantPriceChanges.SingleAsync(p => p.VariantId == capId);
        var command = Command(capId) with { ExpectedPriceChangeId = current.Id };
        var service = new VariantPriceService(database, new FixedTimeProvider(current.EffectiveAt));
        Assert.Equal(PriceChangeStatus.TimestampConflict, (await service.ChangeAsync(command)).Status);
        Assert.Equal(PriceChangeStatus.VariantNotFound,
            (await ChangeAsync(command with { VariantId = Guid.NewGuid() })).Status);

        var redId = await database.ProductVariants.Where(v => v.SKU == "DEMO-TSHIRT-RED-S")
            .Select(v => v.Id).SingleAsync();
        var redPrice = await database.VariantPriceChanges.Where(p => p.VariantId == redId)
            .OrderByDescending(p => p.EffectiveAt).FirstAsync();
        Assert.Equal(PriceChangeStatus.DiscountedPriceConflict, (await ChangeAsync(command with
            { VariantId = redId, ExpectedPriceChangeId = redPrice.Id })).Status);

        database.VariantPriceChanges.Add(new VariantPriceChange
        {
            VariantId = capId, BaseAmount = 20m, SellingAmount = 20m,
            EffectiveAt = DateTime.UtcNow.AddDays(1), Reason = "Zaplanowana cena"
        });
        await database.SaveChangesAsync();
        Assert.Equal(PriceChangeStatus.FuturePriceConflict, (await ChangeAsync(command)).Status);
        Assert.Equal(2, await database.VariantPriceChanges.CountAsync(p => p.VariantId == capId));
        Assert.Equal(0, await database.VariantPriceChanges.CountAsync(p => p.OperationKey != null));
    }

    [PostgresFact]
    public async Task DatabaseRejectsMutationAndDuplicateOperationAndSeederCanReset()
    {
        var variantId = await PrepareAsync(unpriced: true);
        var command = Command(variantId);
        var applied = await ChangeAsync(command);
        var priceId = applied.Price!.Id;
        await using var database = Open();
        var update = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE app.variant_price_changes SET base_amount = 99 WHERE id = {priceId}"));
        Assert.Equal(PostgresErrorCodes.RaiseException, update.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM app.variant_price_changes WHERE id = {priceId}"));
        Assert.Equal(PostgresErrorCodes.RaiseException, delete.SqlState);

        database.VariantPriceChanges.Add(new VariantPriceChange
        {
            VariantId = variantId, BaseAmount = 10m, SellingAmount = 10m,
            EffectiveAt = DateTime.UtcNow, Reason = "Duplikat", OperationKey = command.OperationKey,
            ChangedByUserId = command.ActorUserId
        });
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        database.ChangeTracker.Clear();
        Assert.Equal(1, await database.VariantPriceChanges.CountAsync(p => p.VariantId == variantId));

        var reset = await new DemoCatalogSeeder(database).SeedAsync(
            new DemoSeedOptions(new(3, 3), new(2, 2), new(1, 1)), CancellationToken.None);
        Assert.Equal(4, reset.Created.PriceChanges);
        Assert.Equal(0, await database.VariantPriceChanges.CountAsync(p => p.OperationKey != null));
    }

    private static ChangeVariantPrice Command(Guid variantId) => new(
        variantId, 10.25m, "PLN", "Zmiana testowa", null, Guid.NewGuid(), Guid.NewGuid());

    private static AppDbContext Open()
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(connectionString);
        if (address.Host is not ("127.0.0.1" or "localhost") ||
            address.Database is null || !address.Database.StartsWith("ecomm_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Test wymaga izolowanej lokalnej bazy ecomm_test_*.");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }

    private static async Task<Guid> PrepareAsync(bool unpriced = false)
    {
        await using var database = Open();
        await new DemoCatalogSeeder(database).SeedAsync(
            new DemoSeedOptions(new(3, 3), new(2, 2), new(1, 1)), CancellationToken.None);
        return unpriced ? await AddUnpricedVariantAsync(database) : await database.ProductVariants
            .Where(v => v.SKU == "DEMO-CAP-ONE").Select(v => v.Id).SingleAsync();
    }

    private static async Task<Guid> AddUnpricedVariantAsync(AppDbContext database)
    {
        var product = new Product { Name = "Szkic testowy", Slug = "test-" + Guid.NewGuid().ToString("N") };
        var variant = new ProductVariant { Product = product, SKU = "TEST-" + Guid.NewGuid().ToString("N") };
        database.ProductVariants.Add(variant);
        await database.SaveChangesAsync();
        return variant.Id;
    }

    private static async Task<PriceChangeResult> ChangeAsync(ChangeVariantPrice command)
    {
        await using var database = Open();
        return await new VariantPriceService(database, TimeProvider.System).ChangeAsync(command);
    }

    private static async Task<PriceChangeResult[]> RaceAsync(ChangeVariantPrice first, ChangeVariantPrice second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<PriceChangeResult> Run(ChangeVariantPrice command)
        {
            await start.Task;
            return await ChangeAsync(command);
        }
        var tasks = new[] { Run(first), Run(second) };
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}

[CollectionDefinition("Postgres catalog", DisableParallelization = true)]
public sealed class PostgresCatalogCollection;
