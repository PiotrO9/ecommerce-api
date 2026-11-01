using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog.Management;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class CatalogManagementPostgresTests
{
    internal static readonly Guid Actor = Guid.Parse("09c34bd5-2bfa-433d-a19c-cf957dfc1a19");
    internal static AppDbContext Open()
    {
        var cs = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(cs);
        if (address.Host is not ("127.0.0.1" or "localhost") || address.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Catalog writes require an isolated local ecomm_test_* database.");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
    internal static async Task Seed()
    {
        await using var db = Open();
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
    }
    internal static ProductManagementService Products(AppDbContext db) => new(db, new(db, TimeProvider.System), TimeProvider.System);
    internal static CategoryManagementService Categories(AppDbContext db) => new(db, new(db, TimeProvider.System), TimeProvider.System);
    internal static AttributeManagementService Attributes(AppDbContext db) => new(db, new(db, TimeProvider.System), TimeProvider.System);
    internal static async Task<CatalogResult> Write(Func<AppDbContext, Task<CatalogResult>> action)
    {
        await using var db = Open();
        try { return await action(db); }
        catch (CatalogValidation e) { return new(e.Status, new CatalogError(e.Code, e.Code, e.Field)); }
    }
    internal static CatalogCommandResponse Receipt(CatalogResult result, int status = 200)
    { Assert.Equal(status, result.Status); return Assert.IsType<CatalogCommandResponse>(result.Body); }
    private static ProductCreateRequest Draft(string name = "Nowy produkt", string sku = "NEW-RED-S", Guid[]? categories = null, Guid[]? pairs = null) =>
        new(name, null, categories, new(sku, "Opis", null, [], pairs));

    [PostgresFact]
    public async Task DraftPriceStockPublicationAndHideFormOneConsistentWorkflow()
    {
        await Seed();
        var category = Receipt(await Write(db => Categories(db).CreateAsync(new("Żółte łódki", null, null), Actor, Guid.NewGuid(), default)), 201);
        var created = Receipt(await Write(db => Products(db).CreateAsync(Draft(categories: [category.Id]), Actor, Guid.NewGuid(), default)), 201);
        var id = created.Id; var vid = created.VariantId!.Value;
        Assert.True(created.Version > 1);
        Assert.Equal(409, (await Write(db => Products(db).VariantStatusAsync(vid, new("Published", created.VariantVersion!.Value), Actor, Guid.NewGuid(), default))).Status);
        await using (var db = Open())
        {
            Assert.Equal("zolte-lodki", (await db.Categories.SingleAsync(c => c.Id == category.Id)).Slug);
            Assert.Equal(1, await db.ProductVariants.CountAsync(v => v.ProductId == id));
            var reader = new CatalogManagementReader(db, TimeProvider.System);
            var readiness = Assert.IsType<PublicationReadiness>((await reader.ReadinessAsync(id, false, 1, 20, default)).Body);
            Assert.False(readiness.CanPublish); Assert.Contains(readiness.Blockers, b => b.Code == "no_published_variant");
            var price = await new VariantPriceService(db, TimeProvider.System).ChangeAsync(new(vid, 79.99m, "PLN", "Cena początkowa", null, Guid.NewGuid(), Actor));
            Assert.Equal(PriceChangeStatus.Applied, price.Status);
            Assert.Equal(created.Version, await db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());
        }
        var stock = Receipt(await Write(db => new VariantStockService(db, new(db, TimeProvider.System), TimeProvider.System)
            .SetAsync(vid, new(12, null, "Przyjęcie"), Actor, Guid.NewGuid(), default)), 201);
        Assert.Equal(1, stock.Version);
        var publishedVariant = Receipt(await Write(db => Products(db).VariantStatusAsync(vid, new("Published", created.VariantVersion!.Value), Actor, Guid.NewGuid(), default)));
        Assert.True(publishedVariant.ProductVersion > created.Version);
        var published = Receipt(await Write(db => Products(db).StatusAsync(id, new("Published", publishedVariant.ProductVersion!.Value), Actor, Guid.NewGuid(), default)));
        Assert.Equal(409, (await Write(db => Products(db).VariantStatusAsync(vid, new("Hidden", publishedVariant.Version!.Value), Actor, Guid.NewGuid(), default))).Status);
        Assert.Equal(409, (await Write(db => Products(db).CategoriesAsync(id, new([], published.Version!.Value), Actor, Guid.NewGuid(), default))).Status);
        DateTime? date;
        await using (var db = Open()) date = (await db.Products.SingleAsync(p => p.Id == id)).PublishedAt;
        var hidden = Receipt(await Write(db => Products(db).StatusAsync(id, new("Hidden", published.Version!.Value), Actor, Guid.NewGuid(), default)));
        var shown = Receipt(await Write(db => Products(db).StatusAsync(id, new("Published", hidden.Version!.Value), Actor, Guid.NewGuid(), default)));
        await using (var db = Open())
        {
            Assert.Equal(date, (await db.Products.SingleAsync(p => p.Id == id)).PublishedAt);
            Assert.Equal(CatalogStatus.Published, (await db.ProductVariants.SingleAsync(v => v.Id == vid)).Status);
            Assert.Single(await db.VariantPriceChanges.Where(p => p.VariantId == vid).ToArrayAsync());
            var reader = new CatalogManagementReader(db, TimeProvider.System);
            Assert.True(Assert.IsType<PublicationReadiness>((await reader.ReadinessAsync(id, false, 1, 1, default)).Body).CanPublish);
            var details = Assert.IsType<AdminVariantDetails>((await reader.VariantAsync(vid, default)).Body);
            Assert.Equal(79.99m, details.SellingAmount); Assert.Equal(12, details.Stock!.QuantityOnHand);
            Assert.Equal(shown.Version, details.ProductVersion);
        }
    }

    [PostgresFact]
    public async Task ConcurrentVersionsAndIdempotencyNeverLoseEditsOrDuplicateCreates()
    {
        await Seed(); var key = Guid.NewGuid(); var request = Draft();
        var race = await Task.WhenAll(Write(db => Products(db).CreateAsync(request, Actor, key, default)), Write(db => Products(db).CreateAsync(request, Actor, key, default)));
        Assert.Equal([200, 201], race.Select(r => r.Status).Order());
        var first = Receipt(race.Single(r => r.Status == 201), 201);
        Assert.Equal(first.Id, Receipt(race.Single(r => r.Status == 200)).Id);
        var a = new ProductEditRequest("Pierwsza zmiana", "pierwsza-zmiana", first.Version!.Value);
        var b = new ProductEditRequest("Druga zmiana", "druga-zmiana", first.Version!.Value);
        var editRace = await Task.WhenAll(Write(db => Products(db).EditAsync(first.Id, a, Actor, Guid.NewGuid(), default)), Write(db => Products(db).EditAsync(first.Id, b, Actor, Guid.NewGuid(), default)));
        Assert.Equal([200, 409], editRace.Select(r => r.Status).Order());
        var replay = Receipt(await Write(db => Products(db).CreateAsync(request, Actor, key, default)));
        Assert.True(replay.Replayed); Assert.Equal(first, replay with { Replayed = false });
        Assert.Equal(409, (await Write(db => Products(db).CreateAsync(Draft("Inna nazwa"), Actor, key, default))).Status);
        await using var db = Open();
        Assert.Equal(1, await db.Products.CountAsync(p => p.Id == first.Id));
        Assert.Equal(2, await db.CatalogOperations.CountAsync());
    }

    [PostgresFact]
    public async Task AttributeDictionaryCanonicalizesWithoutCollapsingCaseAndLocksPricedIdentity()
    {
        await Seed();
        var k = Receipt(await Write(db => Attributes(db).CreateKeyAsync(new(" Materiał\u00a0", "Materiał"), Actor, Guid.NewGuid(), default)), 201);
        var pairRace = await Task.WhenAll(
            Write(db => Attributes(db).CreatePairAsync(k.Id, new("\u00a0Żółty\u3000"), Actor, Guid.NewGuid(), default)),
            Write(db => Attributes(db).CreatePairAsync(k.Id, new("Z\u0307o\u0301łty"), Actor, Guid.NewGuid(), default)));
        Assert.Equal([200, 201], pairRace.Select(r => r.Status).Order());
        var pair = Receipt(pairRace.Single(r => r.Status == 201), 201);
        var differentCase = Receipt(await Write(db => Attributes(db).CreatePairAsync(k.Id, new("żółty"), Actor, Guid.NewGuid(), default)), 201);
        Assert.NotEqual(pair.Id, differentCase.Id);
        var item = Receipt(await Write(db => Products(db).CreateAsync(Draft(pairs: [pair.Id, differentCase.Id]), Actor, Guid.NewGuid(), default)), 201);
        var duplicate = await Write(db => Products(db).AddVariantAsync(item.Id,
            new(item.Version!.Value, "OTHER-SKU", null, null, [], [differentCase.Id, pair.Id]), Actor, Guid.NewGuid(), default));
        Assert.Equal(409, duplicate.Status);
        var cleared = Receipt(await Write(db => Products(db).AttributesAsync(item.VariantId!.Value,
            new([], item.VariantVersion!.Value), Actor, Guid.NewGuid(), default)));
        Assert.True(cleared.Version > item.VariantVersion); Assert.True(cleared.ProductVersion > item.Version);
        await using (var db = Open())
        {
            Assert.Equal(PriceChangeStatus.Applied, (await new VariantPriceService(db, TimeProvider.System)
                .ChangeAsync(new(item.VariantId!.Value, 10, null, "Cena", null, Guid.NewGuid(), Actor))).Status);
            Assert.Equal("materiał", (await db.AttributeKeys.SingleAsync(x => x.Id == k.Id)).Code);
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.attribute_pairs SET value='Inne' WHERE id={pair.Id}"));
        }
        Assert.Equal(409, (await Write(db => Products(db).AttributesAsync(item.VariantId!.Value,
            new([pair.Id], cleared.Version!.Value), Actor, Guid.NewGuid(), default))).Status);
    }

    [PostgresFact]
    public async Task CategoriesRejectCyclesInUseAndSlugConflictsAndReplayDeletedResource()
    {
        await Seed();
        var root = Receipt(await Write(db => Categories(db).CreateAsync(new("Root", null, null), Actor, Guid.NewGuid(), default)), 201);
        var child = Receipt(await Write(db => Categories(db).CreateAsync(new("Child", null, root.Id), Actor, Guid.NewGuid(), default)), 201);
        Assert.Equal(409, (await Write(db => Categories(db).ParentAsync(root.Id, new(child.Id, root.Version!.Value), Actor, Guid.NewGuid(), default))).Status);
        Assert.Equal(409, (await Write(db => Categories(db).DeleteAsync(root.Id, root.Version!.Value, Actor, Guid.NewGuid(), default))).Status);
        Assert.Equal(409, (await Write(db => Categories(db).CreateAsync(new("Child", null, root.Id), Actor, Guid.NewGuid(), default))).Status);
        var moved = Receipt(await Write(db => Categories(db).ParentAsync(child.Id, new(null, child.Version!.Value), Actor, Guid.NewGuid(), default)));
        var key = Guid.NewGuid();
        Assert.Equal(204, (await Write(db => Categories(db).DeleteAsync(child.Id, moved.Version!.Value, Actor, key, default))).Status);
        Assert.Equal(204, (await Write(db => Categories(db).DeleteAsync(child.Id, moved.Version!.Value, Actor, key, default))).Status);
        await using var db = Open();
        Assert.False(await db.Categories.AnyAsync(c => c.Id == child.Id));
        Assert.Single(await db.CatalogOperations.Where(o => o.Kind == "category.delete").ToArrayAsync());
    }

    [PostgresFact]
    public async Task StockHasIndependentVersionAndBeforeAfterAudit()
    {
        await Seed();
        var item = Receipt(await Write(db => Products(db).CreateAsync(Draft(), Actor, Guid.NewGuid(), default)), 201);
        var vid = item.VariantId!.Value; var key = Guid.NewGuid();
        Task<CatalogResult> Set(int qty, long? version, Guid operation) => Write(db => new VariantStockService(db, new(db, TimeProvider.System), TimeProvider.System)
            .SetAsync(vid, new(qty, version, "Korekta"), Actor, operation, default));
        var race = await Task.WhenAll(Set(0, null, key), Set(1, null, Guid.NewGuid()));
        Assert.Equal([201, 409], race.Select(r => r.Status).Order());
        var stock = Receipt(race.Single(r => r.Status == 201), 201);
        var editRace = await Task.WhenAll(Set(5, stock.Version, Guid.NewGuid()), Set(7, stock.Version, Guid.NewGuid()));
        Assert.Equal([200, 409], editRace.Select(r => r.Status).Order());
        await using var db = Open();
        Assert.Equal(item.Version, await db.Products.Where(p => p.Id == item.Id).Select(p => p.Version).SingleAsync());
        var operations = await db.CatalogOperations.Where(o => o.Kind == "stock.set").OrderBy(o => o.CreatedAt).ToArrayAsync();
        Assert.Equal(2, operations.Length); Assert.Null(operations[0].QuantityBefore);
        Assert.Equal(operations[0].QuantityAfter, operations[1].QuantityBefore);
        Assert.Equal(Actor, operations[1].ActorUserId); Assert.Equal("Korekta", operations[1].Reason);
        Assert.Equal(409, (await Set(2, stock.Version, Guid.NewGuid())).Status);
    }

    [PostgresFact]
    public async Task DeferredRulesRollbackGraphsAndOperationHistoryAtCommit()
    {
        await Seed();
        await using var db = Open();
        var store = new CatalogCommandStore(db, TimeProvider.System);
        var id = Guid.NewGuid();
        var result = await store.ExecuteAsync("product.create", id, null, new { }, Actor, Guid.NewGuid(), async () =>
        {
            db.Products.Add(new Product { Id = id, Name = "Bez wariantu", Slug = "bez-wariantu" });
            await db.SaveChangesAsync();
            return new(new(false, id, 1), 201);
        }, default);
        Assert.Equal(409, result.Status);
        Assert.False(await db.Products.AnyAsync(p => p.Id == id)); Assert.Empty(await db.CatalogOperations.ToArrayAsync());
        var created = Receipt(await Write(db => Products(db).CreateAsync(Draft(), Actor, Guid.NewGuid(), default)), 201);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE app.product_variants SET status='Published' WHERE id={created.VariantId!.Value}"));
        Assert.Equal("catalog_variant_publication", error.ConstraintName);
        Assert.Equal(CatalogStatus.Draft, (await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == created.VariantId)).Status);
        var second = new ProductVariant { ProductId = created.Id, SKU = "DUPLICATE-EMPTY" };
        db.ProductVariants.Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.ProductVariants.CountAsync(v => v.ProductId == created.Id));
    }

    [PostgresFact]
    public async Task ConcurrentTreeMovesAndSkuClaimsHaveOneWinner()
    {
        await Seed();
        var a = Receipt(await Write(db => Categories(db).CreateAsync(new("Move A", null, null), Actor, Guid.NewGuid(), default)), 201);
        var b = Receipt(await Write(db => Categories(db).CreateAsync(new("Move B", null, null), Actor, Guid.NewGuid(), default)), 201);
        var moves = await Task.WhenAll(
            Write(db => Categories(db).ParentAsync(a.Id, new(b.Id, a.Version!.Value), Actor, Guid.NewGuid(), default)),
            Write(db => Categories(db).ParentAsync(b.Id, new(a.Id, b.Version!.Value), Actor, Guid.NewGuid(), default)));
        Assert.Equal([200, 409], moves.Select(r => r.Status).Order());
        var products = await Task.WhenAll(
            Write(db => Products(db).CreateAsync(Draft("Claim A", "CLAIM-SKU"), Actor, Guid.NewGuid(), default)),
            Write(db => Products(db).CreateAsync(Draft("Claim B", "CLAIM-SKU"), Actor, Guid.NewGuid(), default)));
        Assert.Equal([201, 409], products.Select(r => r.Status).Order());
        await using var db = Open();
        Assert.Equal(1, await db.ProductVariants.CountAsync(v => v.SKU == "CLAIM-SKU"));
        var child = moves[0].Status == 200 ? a.Id : b.Id;
        var parent = child == a.Id ? b.Id : a.Id;
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.categories SET parent_id={child} WHERE id={parent}"));
    }

    [PostgresFact]
    public async Task AdditionalVariantsMetadataAndCategoryAssignmentsAreVisibleInAdminReads()
    {
        await Seed();
        await using var db = Open();
        var category = await db.Categories.SingleAsync(c => c.Slug == "demo-koszulki");
        var red = await db.AttributePairs.Where(p => p.AttributeKey.Code == "demo-kolor" && p.Value == "czerwony").Select(p => p.Id).SingleAsync();
        var item = Receipt(await Write(db => Products(db).CreateAsync(Draft(), Actor, Guid.NewGuid(), default)), 201);
        var second = Receipt(await Write(db => Products(db).AddVariantAsync(item.Id,
            new(item.Version!.Value, "NEW-SECOND", null, null, [], [red]), Actor, Guid.NewGuid(), default)), 201);
        Assert.NotEqual(item.VariantId, second.VariantId);
        var edited = Receipt(await Write(db => Products(db).EditVariantAsync(second.Id,
            new(" NEW-SECOND-EDIT ", "Pełny opis", "Krótki", ["https://example.test/photo.jpg"], second.Version!.Value), Actor, Guid.NewGuid(), default)));
        var assigned = Receipt(await Write(db => Products(db).CategoriesAsync(item.Id,
            new([category.Id], edited.ProductVersion!.Value), Actor, Guid.NewGuid(), default)));
        Assert.Equal(409, (await Write(db => Products(db).AttributesAsync(item.VariantId!.Value,
            new([red], item.VariantVersion!.Value), Actor, Guid.NewGuid(), default))).Status);
        Assert.Equal(409, (await Write(db => Categories(db).DeleteAsync(category.Id, category.Version, Actor, Guid.NewGuid(), default))).Status);
        var reader = new CatalogManagementReader(db, TimeProvider.System);
        var details = Assert.IsType<AdminProductDetails>((await reader.ProductAsync(item.Id, default)).Body);
        Assert.Equal(2, details.Product.VariantCount); Assert.Equal(assigned.Version, details.Product.Version); Assert.Equal([category.Id], details.CategoryIds);
        var variant = Assert.IsType<AdminVariantDetails>((await reader.VariantAsync(second.Id, default)).Body);
        Assert.Equal("NEW-SECOND-EDIT", variant.Variant.Sku); Assert.Equal("Pełny opis", variant.Variant.Description);
        Assert.Equal([red], variant.AttributePairIds); Assert.Null(variant.Stock); Assert.Null(variant.SellingAmount);
        var results = Assert.IsType<AdminPage<AdminProductRow>>((await reader.ProductsAsync(
            new(Q: "SECOND-EDIT", Status: "Draft", CategoryId: category.Id, MissingPrice: true, MissingStock: true, PageSize: 1), default)).Body);
        Assert.Equal(item.Id, Assert.Single(results.Items).Id); Assert.Equal(1, results.Pagination.TotalItems);
        var page = Assert.IsType<AdminPage<AdminVariantSummary>>((await reader.VariantsAsync(item.Id, 2, 1, default)).Body);
        Assert.Single(page.Items); Assert.True(page.Pagination.HasPreviousPage); Assert.False(page.Pagination.HasNextPage);
    }

    [PostgresFact]
    public async Task TriggerVersionsDictionaryEditsAndJournalPrivilegesAreEnforced()
    {
        await Seed();
        var item = Receipt(await Write(db => Products(db).CreateAsync(Draft(), Actor, Guid.NewGuid(), default)), 201);
        var k = Receipt(await Write(db => Attributes(db).CreateKeyAsync(new("code", "Etykieta"), Actor, Guid.NewGuid(), default)), 201);
        var changed = Receipt(await Write(db => Attributes(db).EditKeyAsync(k.Id, new("Nowa etykieta", k.Version!.Value), Actor, Guid.NewGuid(), default)));
        Assert.True(changed.Version > k.Version);
        Assert.Equal(409, (await Write(db => Attributes(db).EditKeyAsync(k.Id, new("Nieaktualna", k.Version!.Value), Actor, Guid.NewGuid(), default))).Status);
        await using var db = Open();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.products SET version=999999 WHERE id={item.Id}");
        Assert.Equal(item.Version + 1, await db.Products.Where(p => p.Id == item.Id).Select(p => p.Version).SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.attribute_keys SET code='different' WHERE id={k.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE app.catalog_operations SET reason='mutated'"));
        var input = "\u00a0Z\u0307o\u0301łty\u3000";
        var sqlValue = await db.Database.SqlQuery<string>($"SELECT app.catalog_canonical({input}) AS \"Value\"").SingleAsync();
        Assert.Equal(CatalogInput.Canonical(input), sqlValue);
        var exposed = await db.Database.SqlQuery<bool>($"SELECT has_table_privilege('anon','app.catalog_operations','SELECT') OR has_table_privilege('authenticated','app.catalog_operations','INSERT') OR has_table_privilege('service_role','app.catalog_operations','SELECT') AS \"Value\"").SingleAsync();
        Assert.False(exposed);
        var result = await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
        Assert.Equal(0, result.Created.CatalogOperations); Assert.Equal(result.Created.Sum(), result.Added);
        Assert.Empty(await db.CatalogOperations.ToArrayAsync());
    }

    [PostgresFact]
    public async Task RawConcurrentDeletesAndConfigurationsPreserveDatabaseGraphInvariants()
    {
        await Seed();
        var item = Receipt(await Write(db => Products(db).CreateAsync(Draft(), Actor, Guid.NewGuid(), default)), 201);
        Guid red, blue;
        await using (var db = Open())
        {
            red = await db.AttributePairs.Where(p => p.AttributeKey.Code == "demo-kolor" && p.Value == "czerwony").Select(p => p.Id).SingleAsync();
            blue = await db.AttributePairs.Where(p => p.AttributeKey.Code == "demo-kolor" && p.Value == "niebieski").Select(p => p.Id).SingleAsync();
        }
        var second = Receipt(await Write(db => Products(db).AddVariantAsync(item.Id,
            new(item.Version!.Value, "RAW-RED", null, null, [], [red]), Actor, Guid.NewGuid(), default)), 201);
        async Task<bool> Raw(Func<AppDbContext, Task> change)
        {
            await using var db = Open();
            try { await change(db); return true; }
            catch (PostgresException e) when (e.SqlState == "P0001") { return false; }
            catch (PostgresException e) when (e.SqlState == "23503" && e.ConstraintName == "FK_variant_offer_events_product_variants_variant_id") { return false; }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "P0001" }) { return false; }
        }
        var deletes = await Task.WhenAll(
            Raw(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.product_variants WHERE id={item.VariantId!.Value}"); }),
            Raw(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app.product_variants WHERE id={second.Id}"); }));
        // Even a new draft already has an immutable offer-lifetime event. Its FK
        // protects both variants from physical deletion; use hiding in the API.
        Assert.Equal(0, deletes.Count(success => success));
        await using (var db = Open()) Assert.Equal(2, await db.ProductVariants.CountAsync(v => v.ProductId == item.Id));
        Task<bool> Insert() => Raw(async db =>
        {
            var variant = new ProductVariant { ProductId = item.Id, SKU = "RAW-BLUE-" + Guid.NewGuid().ToString("N") };
            db.AddRange(variant, new VariantAttributePair { VariantId = variant.Id, AttributePairId = blue });
            await db.SaveChangesAsync();
        });
        var inserts = await Task.WhenAll(Insert(), Insert());
        Assert.Equal(1, inserts.Count(success => success));
        await using (var db = Open()) Assert.Equal(3, await db.ProductVariants.CountAsync(v => v.ProductId == item.Id));
    }
}
