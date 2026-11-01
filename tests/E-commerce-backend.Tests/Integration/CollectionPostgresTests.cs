using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class CollectionPostgresTests
{
    private static readonly Guid Actor = Guid.Parse("f6de1453-e70c-4a87-a20c-a8b5d693b911");
    private static AppDbContext Open()
    {
        var cs = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var parsed = new NpgsqlConnectionStringBuilder(cs);
        if (parsed.Host is not ("localhost" or "127.0.0.1") || parsed.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true) throw new InvalidOperationException("Isolated local database required");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
    private static async Task<Guid> Seed()
    {
        await using var db = Open();
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
        return await db.Collections.Where(c => c.Slug == "demo-kolekcja").Select(c => c.Id).SingleAsync();
    }
    private static async Task<CollectionResult> Change(string kind, Guid id, CollectionWriteRequest? body = null, CollectionProductsRequest? members = null, Guid? key = null)
    {
        await using var db = Open();
        return await new CollectionService(db, TimeProvider.System).MutateAsync(kind, id, body, members, key ?? Guid.NewGuid(), Actor, default);
    }
    private static async Task<PromotionResult> Promo(string kind, Guid id, PromotionDraftRequest? body = null, PromotionVersionRequest? version = null, Guid? key = null)
    {
        await using var db = Open();
        return await new PromotionService(db, TimeProvider.System).MutateAsync(kind, id, body, version, key ?? Guid.NewGuid(), Actor, default);
    }
    private static async Task<PromotionPreviewResponse> Preview(Guid id)
    {
        await using var db = Open();
        var result = await new PromotionService(db, TimeProvider.System).PreviewAsync(id, 1, 1, default);
        Assert.Equal(200, result.Status);
        return Assert.IsType<PromotionPreviewResponse>(result.Body);
    }
    private static PromotionActivationRequest Activation(PromotionPreviewResponse p) => new() { ExpectedVersion = p.Version, ExpectedCollectionVersion = p.CollectionVersion, Variants = p.ExpectedPrices.ToList() };
    private static async Task<PromotionResponse> Draft(Guid collection)
    {
        var result = await Promo("create", Guid.Empty, new() { Scope = "collection", CollectionId = collection, Name = "Kolekcja -20%", DiscountType = "percentage", DiscountValue = 20 });
        Assert.Equal(201, result.Status);
        return Assert.IsType<PromotionCommandResponse>(result.Body).Promotion;
    }

    [PostgresFact]
    public async Task CollectionWritesVersioningAndReplayAreAtomic()
    {
        await Seed();
        var key = Guid.NewGuid();
        var body = new CollectionWriteRequest { DisplayName = " Nowa ", Slug = " NEW-COLLECTION " };
        var result = await Change("create", Guid.Empty, body, key: key);
        Assert.Equal(201, result.Status);
        var created = Assert.IsType<CollectionCommandResponse>(result.Body).Collection;
        Assert.Equal("new-collection", created.Slug); Assert.Equal(1, created.Version);
        Assert.True(Assert.IsType<CollectionCommandResponse>((await Change("create", Guid.Empty, body, key: key)).Body).Replayed);
        Assert.Equal(409, (await Change("create", Guid.Empty, body)).Status);
        await using var db = Open();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.collections SET status='Published' WHERE id={created.Id}"));
        var product = await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
        key = Guid.NewGuid();
        var members = new CollectionProductsRequest { ExpectedVersion = 1, ProductIds = [product] };
        var linked = await Change("members", created.Id, members: members, key: key);
        Assert.Equal(200, linked.Status);
        var version = Assert.IsType<CollectionCommandResponse>(linked.Body).Collection.Version;
        Assert.True(version > 1);
        Assert.True(Assert.IsType<CollectionCommandResponse>((await Change("members", created.Id, members: members, key: key)).Body).Replayed);
        Assert.Equal(409, (await Change("members", created.Id, members: members)).Status);
        Assert.Equal(200, (await Change("update", created.Id, new() { DisplayName = "Nowa", Slug = created.Slug, Status = "Published", ExpectedVersion = version })).Status);
        var rows = Assert.IsType<CollectionProductsResponse>((await new CollectionService(db, TimeProvider.System).MembersAsync(created.Id, 1, 1, default)).Body);
        Assert.Single(rows.Items); Assert.Equal([product], rows.ProductIds);
        Assert.Equal(409, (await Change("members", created.Id, members: new() { ExpectedVersion = rows.CollectionVersion, ProductIds = [] })).Status);
        Assert.Single(await db.CollectionProducts.Where(l => l.CollectionId == created.Id).ToArrayAsync());
        var events = await db.VariantOfferEvents.CountAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.collections SET version=999999, status='Hidden' WHERE id={created.Id}");
        Assert.Equal(rows.CollectionVersion + 1, await db.Collections.Where(c => c.Id == created.Id).Select(c => c.Version).SingleAsync());
        Assert.Equal(events, await db.VariantOfferEvents.CountAsync());
        Assert.Equal(404, (await new CollectionService(db, TimeProvider.System).GetAsync(false, created.Id, null, default)).Status);
        Assert.NotNull(Assert.IsType<OkObjectResult>(await new ProductsController(db).Get(product, null, default)).Value);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.collection_operations SET kind='changed' WHERE collection_id={created.Id}"));
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
        Assert.Empty(await db.CollectionOperations.ToArrayAsync());
    }

    [PostgresFact]
    public async Task PublicCollectionReadsReusePriceAttributesPagingAndCategoryIntersection()
    {
        var collection = await Seed();
        await using var db = Open();
        var service = new CollectionService(db, TimeProvider.System);
        var byId = Assert.IsType<CollectionResponse>((await service.GetAsync(false, collection, null, default)).Body);
        var bySlug = Assert.IsType<CollectionResponse>((await service.GetAsync(false, null, " DEMO-KOLEKCJA ", default)).Body);
        Assert.Equal(byId, bySlug); Assert.Equal(2, byId.ProductCount);
        var publicList = Assert.IsType<CollectionListResponse>((await service.ListAsync(false, 1, 100, null, default)).Body);
        Assert.Equal(4, publicList.TotalItems); Assert.All(publicList.Items, c => Assert.Equal("Published", c.Status));
        Assert.Single(Assert.IsType<CollectionListResponse>((await service.ListAsync(true, 1, 100, "Draft", default)).Body).Items);
        var controller = new ProductsController(db);
        var page1 = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(await controller.GetByCollection(collection, null, 1, 1, null, "name-asc", null, null, default)).Value);
        var page2 = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(await controller.GetByCollection(null, "demo-kolekcja", 2, 1, null, "name-asc", null, null, default)).Value);
        Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        var cap = await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
        var category = await db.ProductCategories.Where(l => l.ProductId == cap).Select(l => l.CategoryId).SingleAsync();
        var search = Assert.IsType<ProductSearchResponse>(Assert.IsType<OkObjectResult>(await controller.Search(new() {
            CollectionSlug = "demo-kolekcja", CategoryId = category, Q = "DEMO-CAP-ONE", MinPrice = 50, MaxPrice = 50 }, default)).Value);
        Assert.Equal(cap, Assert.Single(search.Items).Id);
        var noCrossVariant = Assert.IsType<ProductSearchResponse>(Assert.IsType<OkObjectResult>(await controller.Search(new() {
            CollectionId = collection, Q = "DEMO-CAP-ONE", MinPrice = 72, MaxPrice = 72 }, default)).Value);
        Assert.Empty(noCrossVariant.Items);
        Assert.IsType<OkObjectResult>(await controller.GetFilterOptions(null, null, default, collection));
        var hidden = await db.Collections.Where(c => c.Status == "Hidden").Select(c => c.Id).SingleAsync();
        Assert.IsType<NotFoundObjectResult>(await controller.GetByCollection(hidden, null, null, null, null, null, null, null, default));
        Assert.IsType<NotFoundObjectResult>(await controller.Search(new() { CollectionId = hidden }, default));
        var memberIds = await db.CollectionProducts.Where(l => l.CollectionId == collection).Select(l => l.ProductId).ToArrayAsync();
        await db.Products.Where(p => memberIds.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Hidden));
        Assert.Equal(0, Assert.IsType<CollectionResponse>((await service.GetAsync(false, collection, null, default)).Body).ProductCount);
        Assert.Empty(Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(await controller.GetByCollection(collection, null, 1, 20, null, null, null, null, default)).Value).Items);
    }

    [PostgresFact]
    public async Task PromotionFreezesCollectionScopeAndEndsAfterMembershipRemovalAndHiding()
    {
        var collection = await Seed();
        var draft = await Draft(collection);
        var preview = await Preview(draft.Id);
        Assert.True(preview.CanActivate); Assert.Equal(4, preview.TotalVariants); Assert.Single(preview.Items);
        Assert.NotEmpty(preview.Excluded!);
        var activation = Activation(preview); var key = Guid.NewGuid();
        Assert.Equal(200, (await Promo("activate", draft.Id, version: activation, key: key)).Status);
        await using var db = Open();
        var service = new CollectionService(db, TimeProvider.System);
        var current = Assert.IsType<CollectionResponse>((await service.GetAsync(true, collection, null, default)).Body);
        var next = await db.Products.Where(p => p.Slug == "demo-produkt-12").Select(p => p.Id).SingleAsync();
        var changed = await Change("members", collection, members: new() { ExpectedVersion = current.Version, ProductIds = [next] });
        Assert.Equal(200, changed.Status);
        await db.Collections.Where(c => c.Id == collection).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Hidden"));
        var frozen = await db.PromotionVariants.Where(m => m.PromotionId == draft.Id).Select(m => m.VariantId).ToArrayAsync();
        Assert.Equal(preview.ExpectedPrices.Select(x => x.VariantId).Order(), frozen.Order());
        var response = Assert.IsType<PromotionCommandResponse>((await Promo("activate", draft.Id, version: activation, key: key)).Body);
        Assert.True(response.Replayed);
        Assert.Equal(200, (await Promo("end", draft.Id, version: new() { ExpectedVersion = 2 })).Status);
        Assert.Equal(4, await db.PromotionVariants.CountAsync(m => m.PromotionId == draft.Id && m.EndedAt != null));
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == draft.Id && db.ProductVariants.Any(v => v.Id == m.VariantId && v.ProductId == next)));
        Assert.Equal(409, (await Promo("activate", draft.Id, version: new PromotionActivationRequest { ExpectedVersion = 3, ExpectedCollectionVersion = preview.CollectionVersion, Variants = activation.Variants })).Status);
    }

    [PostgresFact]
    public async Task CollectionVersionAndOverlappingPromotionsCannotBeBypassed()
    {
        var collection = await Seed();
        var draft = await Draft(collection); var preview = await Preview(draft.Id);
        await using var db = Open();
        var oldVersion = preview.CollectionVersion;
        var next = await db.Products.Where(p => p.Slug == "demo-produkt-12").Select(p => p.Id).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.collection_products(collection_id,product_id,created_at) VALUES ({collection},{next},now())");
        Assert.True(await db.Collections.Where(c => c.Id == collection).Select(c => c.Version).SingleAsync() > oldVersion);
        Assert.Equal(409, (await Promo("activate", draft.Id, version: Activation(preview))).Status);
        var fresh = await Preview(draft.Id);
        var product = await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
        var direct = Assert.IsType<PromotionCommandResponse>((await Promo("create", Guid.Empty, new() { ProductId = product, Name = "Konflikt", DiscountType = "percentage", DiscountValue = 20 })).Body).Promotion;
        var directPreview = await Preview(direct.Id);
        var a = Promo("activate", draft.Id, version: Activation(fresh));
        var b = Promo("activate", direct.Id, version: Activation(directPreview));
        var results = await Task.WhenAll(a, b);
        Assert.Single(results, x => x.Status == 200); Assert.Single(results, x => x.Status == 409);
        var second = await db.Collections.Where(c => c.Slug == "demo-pomysly-na-prezent").Select(c => c.Id).SingleAsync();
        var competing = await Preview((await Draft(second)).Id);
        Assert.False(competing.CanActivate);
        Assert.Contains(competing.Items, item => item.Errors.Contains("active_promotion"));
    }

    [PostgresFact]
    public async Task FailedMultiProductActivationRollsBackAndSameKeyCanRetry()
    {
        var collection = await Seed();
        var draft = await Draft(collection); var preview = await Preview(draft.Id);
        await using var db = Open();
        var count = await db.VariantPriceChanges.CountAsync();
        var key = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION app.test_reject_collection_price() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.promotion_operation_id IS NOT NULL AND EXISTS
              (SELECT 1 FROM app.variant_price_changes WHERE promotion_operation_id=NEW.promotion_operation_id) THEN
              RAISE EXCEPTION 'Injected collection price failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER test_reject_collection_price BEFORE INSERT ON app.variant_price_changes
              FOR EACH ROW EXECUTE FUNCTION app.test_reject_collection_price();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Promo("activate", draft.Id, version: Activation(preview), key: key));
            Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
            Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == draft.Id));
            Assert.Equal("Draft", await db.Promotions.Where(p => p.Id == draft.Id).Select(p => p.Status).SingleAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_reject_collection_price ON app.variant_price_changes; DROP FUNCTION app.test_reject_collection_price();"); }
        Assert.Equal(200, (await Promo("activate", draft.Id, version: Activation(preview), key: key)).Status);
    }

    [PostgresFact]
    public async Task MembershipRaceEitherInvalidatesPreviewOrPreservesFrozenScope()
    {
        var collection = await Seed();
        var draft = await Draft(collection); var preview = await Preview(draft.Id);
        await using var db = Open();
        var cap = await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
        var change = Change("members", collection, members: new() { ExpectedVersion = preview.CollectionVersion, ProductIds = [cap] });
        var start = Promo("activate", draft.Id, version: Activation(preview));
        Assert.Equal(200, (await change).Status);
        var result = await start;
        Assert.Contains(result.Status, new[] { 200, 409 });
        var frozen = await db.PromotionVariants.Where(m => m.PromotionId == draft.Id).Select(m => m.VariantId).ToArrayAsync();
        if (result.Status == 200) Assert.Equal(preview.ExpectedPrices.Select(p => p.VariantId).Order(), frozen.Order());
        else Assert.Empty(frozen);
    }

    [PostgresFact]
    public async Task ConcurrentCollectionPromotionsAndSlugCreationHaveSingleWinner()
    {
        var collection = await Seed();
        await using var db = Open();
        var other = await db.Collections.Where(c => c.Slug == "demo-pomysly-na-prezent").Select(c => c.Id).SingleAsync();
        var a = await Draft(collection); var b = await Draft(other);
        var ap = await Preview(a.Id); var bp = await Preview(b.Id);
        var results = await Task.WhenAll(Promo("activate", a.Id, version: Activation(ap)), Promo("activate", b.Id, version: Activation(bp)));
        Assert.Single(results, r => r.Status == 200); Assert.Single(results, r => r.Status == 409);
        var request = new CollectionWriteRequest { DisplayName = "Równolegle", Slug = "concurrent-collection" };
        var writes = await Task.WhenAll(Change("create", Guid.Empty, request), Change("create", Guid.Empty, request));
        Assert.Single(writes, r => r.Status == 201); Assert.Single(writes, r => r.Status == 409);
    }

    [PostgresFact]
    public async Task LargeSeedAndCollectionVariantLimitAreEnforcedBeforeHistoryCalculation()
    {
        await using var db = Open();
        var seeded = await new DemoCatalogSeeder(db).SeedAsync(new(new(80, 80), new(500, 500), new(6, 6)), default);
        Assert.Equal(3000, seeded.Created.Variants);
        await db.Products.Where(p => p.Slug == "demo-produkt-10").ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Published));
        var product = new Product { Name = "Limit", Slug = "limit-" + Guid.NewGuid().ToString("N") };
        var variant = new ProductVariant { Product = product, SKU = "LIMIT-" + Guid.NewGuid().ToString("N") };
        db.ProductVariants.Add(variant);
        db.ProductCategories.Add(new() { Product = product, CategoryId = await db.Categories.Select(c => c.Id).FirstAsync() });
        db.VariantPriceChanges.Add(new() { Variant = variant, BaseAmount = 100, SellingAmount = 100, EffectiveAt = DateTime.UtcNow, Reason = "Limit test" });
        await db.SaveChangesAsync();
        await db.ProductVariants.Where(v => v.Id == variant.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Published));
        await db.Products.Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Published));
        var c = Assert.IsType<CollectionCommandResponse>((await Change("create", Guid.Empty, new() { DisplayName = "Duża", Slug = "large-limit" })).Body).Collection;
        c = Assert.IsType<CollectionCommandResponse>((await Change("members", c.Id, members: new() { ExpectedVersion = c.Version, ProductIds = await db.Products.Select(p => p.Id).ToListAsync() })).Body).Collection;
        Assert.Equal(200, (await Change("update", c.Id, new() { DisplayName = c.DisplayName, Slug = c.Slug, Status = "Published", ExpectedVersion = c.Version })).Status);
        var preview = await Preview((await Draft(c.Id)).Id);
        Assert.False(preview.CanActivate); Assert.Contains("variant_limit_exceeded", preview.Errors); Assert.Empty(preview.Items);
    }

    [PostgresFact]
    public async Task LegacyFingerprintAndSavedResponseReplayAfterTargetEdit()
    {
        var collection = await Seed();
        await using var db = Open();
        var product = await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
        var key = Guid.NewGuid();
        var body = new PromotionDraftRequest { ProductId = product, Name = "Legacy", DiscountType = "amount", DiscountValue = 10 };
        var created = Assert.IsType<PromotionCommandResponse>((await Promo("create", Guid.Empty, body, key: key)).Body).Promotion;
        var invalidScope = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.promotions SET scope='collection', collection_id={collection} WHERE id={created.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidScope.SqlState);
        // Exact pre-collection serialized request shape, including null variants/version.
        var oldJson = JsonSerializer.Serialize(new { kind = "create", id = Guid.Empty, productId = product, name = "Legacy", type = "amount", value = "10", expectedVersion = (long?)null, variants = (object?)null });
        var oldHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldJson)));
        Assert.Equal(oldHash, await db.PromotionOperations.Where(o => o.OperationKey == key && o.ActorUserId == Actor).Select(o => o.RequestHash).SingleAsync());
        var oldResponse = JsonSerializer.Serialize(new { replayed = false, promotion = new { id = created.Id, productId = product,
            name = "Legacy", discountType = "amount", discountValue = 10, currency = "PLN", status = "Draft", version = 1,
            createdAt = created.CreatedAt, updatedAt = created.UpdatedAt, activatedAt = (DateTime?)null, endedAt = (DateTime?)null, variants = Array.Empty<object>() } });
        var legacyKey = Guid.NewGuid();
        db.PromotionOperations.Add(new() { PromotionId = created.Id, ActorUserId = Actor, OperationKey = legacyKey, Kind = "create", RequestHash = oldHash, ResponseJson = oldResponse, StatusCode = 201, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(200, (await Promo("update", created.Id, new() { Scope = "collection", CollectionId = collection, Name = "Nowy cel", DiscountType = "amount", DiscountValue = 10, ExpectedVersion = 1 })).Status);
        var replay = Assert.IsType<PromotionCommandResponse>((await Promo("create", Guid.Empty, body, key: legacyKey)).Body);
        Assert.True(replay.Replayed); Assert.Equal("product", replay.Promotion.Scope); Assert.Equal(product, replay.Promotion.ProductId);
        Assert.Equal("collection", await db.Promotions.Where(p => p.Id == created.Id).Select(p => p.Scope).SingleAsync());
    }
}
