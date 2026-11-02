using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class PromotionPostgresTests
{
    private static readonly Guid Actor = Guid.Parse("a0324d14-90b8-4f4f-a973-9f011ec87d01");
    private static AppDbContext Open()
    {
        var connection = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var cs = new NpgsqlConnectionStringBuilder(connection);
        if (cs.Host is not ("127.0.0.1" or "localhost") || cs.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Use isolated local ecomm_test_* database");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    }
    private static async Task<Guid> SeedAsync()
    {
        await using var db = Open();
        var result = await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(8, 8), new(3, 3)), default);
        Assert.Equal(result.Added, result.Created.Sum());
        Assert.Equal(result.Created.OfferEvents, await db.VariantOfferEvents.CountAsync());
        return await db.Products.Where(p => p.Slug == "demo-czapka").Select(p => p.Id).SingleAsync();
    }
    private static async Task<PromotionResult> Mutate(string kind, Guid id, PromotionDraftRequest? draft = null,
        PromotionVersionRequest? version = null, Guid? key = null)
    {
        await using var db = Open();
        return await new PromotionService(db, TimeProvider.System).MutateAsync(kind, id, draft, version, key ?? Guid.NewGuid(), Actor, default);
    }
    private static async Task<PromotionResponse> Create(Guid product, string type = "percentage", decimal value = 20)
    {
        var result = await Mutate("create", Guid.Empty, new() { ProductId = product, Name = "Test promocji", DiscountType = type, DiscountValue = value });
        Assert.Equal(201, result.Status);
        return Assert.IsType<PromotionCommandResponse>(result.Body).Promotion;
    }
    private static async Task<PromotionPreviewResponse> Preview(Guid id)
    {
        await using var db = Open();
        var result = await new PromotionService(db, TimeProvider.System).PreviewAsync(id, 1, 1, default);
        Assert.Equal(200, result.Status);
        return Assert.IsType<PromotionPreviewResponse>(result.Body);
    }
    private static PromotionActivationRequest Activation(PromotionPreviewResponse p) => new() { ExpectedVersion = p.Version, Variants = p.ExpectedPrices.ToList() };

    [PostgresFact]
    public async Task FullLifecyclePreservesReferenceAndAllPublicReadPaths()
    {
        var product = await SeedAsync();
        var draft = await Create(product);
        var preview = await Preview(draft.Id);
        Assert.True(preview.CanActivate);
        Assert.Equal(3, preview.TotalVariants);
        Assert.Single(preview.Items);
        Assert.Equal(3, preview.ExpectedPrices.Count);
        var key = Guid.NewGuid();
        var activation = Activation(preview);
        var started = await Mutate("activate", draft.Id, version: activation, key: key);
        Assert.Equal(200, started.Status);
        var active = Assert.IsType<PromotionCommandResponse>(started.Body).Promotion;
        Assert.Equal("Active", active.Status);
        Assert.Equal(2, active.Version);
        var replay = await Mutate("activate", draft.Id, version: activation, key: key);
        Assert.True(Assert.IsType<PromotionCommandResponse>(replay.Body).Replayed);
        Assert.Equal(409, (await Mutate("end", draft.Id, version: new() { ExpectedVersion = 2 }, key: key)).Status);

        await using var db = Open();
        var priceCount = await db.VariantPriceChanges.CountAsync();
        var controller = new ProductsController(db);
        var details = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(await controller.Get(product, null, default)).Value);
        Assert.All(details.Variants, v =>
        {
            var price = Assert.Single(v.CurrentPrices);
            Assert.NotNull(price.Reduction);
            Assert.Equal(price.BaseAmount!.Value, price.Reduction.PriorAmount);
            Assert.Equal(20, price.Reduction.SavingPercent);
        });
        var catalog = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCatalog(1, 100, null, "price-asc", null, null, default)).Value);
        var card = Assert.Single(catalog.Items, p => p.Id == product);
        var cardPrice = Assert.Single(card.PricesFrom);
        var sameVariant = details.Variants.Single(v => v.Id == cardPrice.VariantId);
        Assert.Equal(Assert.Single(sameVariant.CurrentPrices), cardPrice);
        var category = await db.ProductCategories.Where(c => c.ProductId == product).Select(c => c.CategoryId).SingleAsync();
        var branch = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetByCategory(category, null, 1, 100, null, null, null, null, default)).Value);
        Assert.Equal(cardPrice, Assert.Single(branch.Items.Single(p => p.Id == product).PricesFrom));
        var search = Assert.IsType<ProductSearchResponse>(Assert.IsType<OkObjectResult>(
            await controller.Search(new() { Q = "Czapka", MinPrice = cardPrice.Amount, MaxPrice = cardPrice.Amount }, default)).Value);
        Assert.Equal(cardPrice, Assert.Single(search.Items.Single(p => p.Id == product).PricesFrom));
        var stored = await db.Products.AsNoTracking().Include(p => p.ProductCategories).ThenInclude(c => c.Category)
            .Include(p => p.Variants).ThenInclude(v => v.PriceChanges).ThenInclude(c => c.PriceReduction)
            .AsSplitQuery().SingleAsync(p => p.Id == product);
        var after31Days = ProductDetailsBuilder.Build(stored, DateTime.UtcNow.AddDays(31))!;
        Assert.Equal(cardPrice.Reduction, Assert.Single(after31Days.Variants.Single(v => v.Id == cardPrice.VariantId).CurrentPrices).Reduction);
        var currentId = preview.ExpectedPrices[0].VariantId;
        var current = await db.VariantPriceChanges.Where(p => p.VariantId == currentId).OrderByDescending(p => p.EffectiveAt).FirstAsync();
        Assert.Equal(PriceChangeStatus.DiscountedPriceConflict, (await new VariantPriceService(db, TimeProvider.System)
            .ChangeAsync(new(currentId, 100, "PLN", "Test", current.Id, Guid.NewGuid(), Actor))).Status);

        await db.Products.Where(p => p.Id == product).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Hidden));
        var ended = await Mutate("end", draft.Id, version: new() { ExpectedVersion = 2 });
        Assert.Equal(200, ended.Status);
        Assert.Equal("Ended", Assert.IsType<PromotionCommandResponse>(ended.Body).Promotion.Status);
        Assert.Equal(priceCount + 3, await db.VariantPriceChanges.CountAsync());
        var oldReplay = await Mutate("activate", draft.Id, version: activation, key: key);
        Assert.Equal("Active", Assert.IsType<PromotionCommandResponse>(oldReplay.Body).Promotion.Status);
        Assert.Equal("Ended", await db.Promotions.Where(p => p.Id == draft.Id).Select(p => p.Status).SingleAsync());
        await db.Products.Where(p => p.Id == product).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Published));
        var after = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(await controller.Get(product, null, default)).Value);
        Assert.All(after.Variants, v => { var price = Assert.Single(v.CurrentPrices); Assert.Null(price.Reduction); Assert.Equal(price.BaseAmount!.Value, price.Amount); });
        Assert.Equal(409, (await Mutate("activate", draft.Id, version: new PromotionActivationRequest { ExpectedVersion = 3, Variants = activation.Variants })).Status);
    }

    [PostgresFact]
    public async Task PreviewValidationAndFailedActivationAreAtomic()
    {
        var product = await SeedAsync();
        var invalid = await Create(product, "amount", 60);
        var preview = await Preview(invalid.Id);
        Assert.False(preview.CanActivate);
        await using var db = Open();
        var count = await db.VariantPriceChanges.CountAsync();
        Assert.Equal(409, (await Mutate("activate", invalid.Id, version: Activation(preview))).Status);
        Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
        Assert.Empty(await db.PromotionVariants.Where(m => m.PromotionId == invalid.Id).ToArrayAsync());
        var valid = await Create(product);
        var current = await Preview(valid.Id);
        var wrong = Activation(current);
        wrong.Variants![0] = wrong.Variants[0] with { ExpectedPriceChangeId = Guid.NewGuid() };
        Assert.Equal(409, (await Mutate("activate", valid.Id, version: wrong)).Status);
        Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
        var shortProduct = await db.Products.Where(p => p.Slug == "demo-produkt-06").Select(p => p.Id).SingleAsync();
        Assert.Equal("SinceFirstOffer", Assert.Single((await Preview((await Create(shortProduct)).Id)).Items).ReferenceBasis);
        var unknownProduct = await db.Products.Where(p => p.Slug == "demo-produkt-07").Select(p => p.Id).SingleAsync();
        var unknown = await Preview((await Create(unknownProduct)).Id);
        Assert.False(unknown.CanActivate);
        Assert.Contains("incomplete_or_empty_offer_history", Assert.Single(unknown.Items).Errors);
        var list = await new PromotionService(db, TimeProvider.System).ListAsync(1, 2, "Draft", product, default);
        Assert.Equal(2, Assert.IsType<PromotionListResponse>(list.Body).Items.Count);
        Assert.Equal(400, (await new PromotionService(db, TimeProvider.System).ListAsync(0, 2, null, null, default)).Status);
    }

    [PostgresFact]
    public async Task ConcurrentActivationsCannotShareVariantsAndConstraintGuardsDirectWrites()
    {
        var product = await SeedAsync();
        var first = await Create(product); var second = await Create(product);
        var a = Activation(await Preview(first.Id)); var b = Activation(await Preview(second.Id));
        var results = await Task.WhenAll(Mutate("activate", first.Id, version: a), Mutate("activate", second.Id, version: b));
        Assert.Single(results, r => r.Status == 200);
        Assert.Single(results, r => r.Status == 409);
        await using var db = Open();
        Assert.Equal(1, await db.Promotions.CountAsync(p => p.ProductId == product && p.Status == "Active"));
        var winner = await db.Promotions.Where(p => p.ProductId == product && p.Status == "Active").Select(p => p.Id).SingleAsync();
        var loser = winner == first.Id ? second.Id : first.Id;
        var member = await db.PromotionVariants.FirstAsync(m => m.PromotionId == winner);
        db.PromotionVariants.Add(new() { PromotionId = loser, VariantId = member.VariantId, StartedAt = DateTime.UtcNow });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var reference = await db.PriceReductions.FirstAsync(r => r.PromotionVariantId == member.Id);
        var mutation = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.price_reductions SET prior_amount = 1 WHERE id = {reference.Id}"));
        Assert.Equal(PostgresErrorCodes.RaiseException, mutation.SqlState);
        var reset = await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(8, 8), new(3, 3)), default);
        Assert.Equal(reset.Created.Promotions, await db.Promotions.CountAsync());
        Assert.Equal(reset.Created.OfferEvents, await db.VariantOfferEvents.CountAsync());
    }

    [PostgresFact]
    public async Task SeederResponseIncludesTriggerEventsAndAllPromotionRecords()
    {
        await using var db = Open();
        var result = await new DemoCatalogSeeder(db).SeedAsync(new(new(25, 25), new(200, 200), new(1, 1)), default);
        Assert.Equal(2093, result.Added);
        Assert.Equal(602, result.Created.OfferEvents);
        Assert.Equal(result.Created.Collections, await db.Collections.CountAsync());
        Assert.Equal(result.Created.CollectionProductLinks, await db.CollectionProducts.CountAsync());
        Assert.Equal(result.Created.CollectionOperations, await db.CollectionOperations.CountAsync());
        Assert.Equal(result.Created.PriceChanges, await db.VariantPriceChanges.CountAsync());
        Assert.Equal(result.Created.OfferEvents, await db.VariantOfferEvents.CountAsync());
        Assert.Equal(result.Created.Promotions, await db.Promotions.CountAsync());
        Assert.Equal(result.Created.PromotionVariants, await db.PromotionVariants.CountAsync());
        Assert.Equal(result.Created.PriceReductions, await db.PriceReductions.CountAsync());
        Assert.Equal(result.Created.PromotionOperations, await db.PromotionOperations.CountAsync());
        Assert.Equal(result.Created.PromotionExclusions, await db.PromotionExclusions.CountAsync());
        Assert.Equal(result.Created.PromotionConflicts, await db.PromotionConflicts.CountAsync());
        Assert.Equal(result.Added, result.Created.Sum());
    }

    [PostgresFact]
    public async Task PublishingVariantLaterDoesNotExpandFrozenPromotionScope()
    {
        var product = await SeedAsync();
        await using var db = Open();
        var variant = await db.ProductVariants.Where(v => v.ProductId == product).Select(v => v.Id).FirstAsync();
        await db.ProductVariants.Where(v => v.Id == variant).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Hidden));
        var draft = await Create(product);
        var preview = await Preview(draft.Id);
        Assert.Equal(2, preview.TotalVariants);
        Assert.Equal(200, (await Mutate("activate", draft.Id, version: Activation(preview))).Status);
        await db.ProductVariants.Where(v => v.Id == variant).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Published));
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == draft.Id && m.VariantId == variant));
        var details = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(await new ProductsController(db).Get(product, null, default)).Value);
        Assert.Null(Assert.Single(details.Variants.Single(v => v.Id == variant).CurrentPrices).Reduction);
        Assert.Equal(200, (await Mutate("end", draft.Id, version: new() { ExpectedVersion = 2 })).Status);
    }

    [PostgresFact]
    public async Task FailedDatabaseWriteRollsBackEntireActivationAndSameKeyCanRetry()
    {
        var product = await SeedAsync();
        var draft = await Create(product);
        var activation = Activation(await Preview(draft.Id));
        var key = Guid.NewGuid();
        await using var db = Open();
        var count = await db.VariantPriceChanges.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION app.test_reject_promo_price() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.promotion_operation_id IS NOT NULL AND EXISTS
              (SELECT 1 FROM app.variant_price_changes WHERE promotion_operation_id = NEW.promotion_operation_id) THEN
              RAISE EXCEPTION 'Injected write failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER test_reject_promo_price BEFORE INSERT ON app.variant_price_changes
              FOR EACH ROW EXECUTE FUNCTION app.test_reject_promo_price();
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Mutate("activate", draft.Id, version: activation, key: key));
            Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
            Assert.Equal("Draft", await db.Promotions.Where(p => p.Id == draft.Id).Select(p => p.Status).SingleAsync());
            Assert.False(await db.PromotionVariants.AnyAsync(p => p.PromotionId == draft.Id));
            Assert.False(await db.PromotionOperations.AnyAsync(o => o.OperationKey == key));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_reject_promo_price ON app.variant_price_changes; DROP FUNCTION app.test_reject_promo_price();");
        }
        Assert.Equal(200, (await Mutate("activate", draft.Id, version: activation, key: key)).Status);
    }

    [PostgresFact]
    public async Task ActivationAndOrdinaryPriceRaceCannotBothSucceed()
    {
        var product = await SeedAsync();
        var draft = await Create(product);
        var preview = await Preview(draft.Id);
        var expected = preview.ExpectedPrices[0];
        async Task<PriceChangeResult> Change()
        {
            await using var db = Open();
            return await new VariantPriceService(db, TimeProvider.System).ChangeAsync(
                new(expected.VariantId, 105, "PLN", "Równoczesna zmiana", expected.ExpectedPriceChangeId, Guid.NewGuid(), Actor));
        }
        var change = Change();
        var start = Mutate("activate", draft.Id, version: Activation(preview));
        var priceResult = await change;
        var promotionResult = await start;
        Assert.True((promotionResult.Status == 200 && priceResult.Status == PriceChangeStatus.DiscountedPriceConflict)
            || (promotionResult.Status == 409 && priceResult.Status == PriceChangeStatus.Applied));
    }

    [PostgresFact]
    public async Task DraftVersionsReplayAndEndedReplayPreserveOriginalResults()
    {
        var product = await SeedAsync();
        var draft = await Create(product);
        var key = Guid.NewGuid();
        var update = new PromotionDraftRequest { ProductId = product, Name = "Nowa nazwa", DiscountType = "amount", DiscountValue = 10, ExpectedVersion = 1 };
        Assert.Equal(200, (await Mutate("update", draft.Id, update, key: key)).Status);
        Assert.True(Assert.IsType<PromotionCommandResponse>((await Mutate("update", draft.Id, update, key: key)).Body).Replayed);
        Assert.Equal(409, (await Mutate("update", draft.Id, update)).Status);
        var preview = await Preview(draft.Id);
        Assert.Equal(2, preview.Version);
        Assert.Equal(200, (await Mutate("activate", draft.Id, version: Activation(preview))).Status);
        key = Guid.NewGuid();
        var end = new PromotionVersionRequest { ExpectedVersion = 3 };
        Assert.Equal(200, (await Mutate("end", draft.Id, version: end, key: key)).Status);
        await using var db = Open();
        var count = await db.VariantPriceChanges.CountAsync();
        Assert.True(Assert.IsType<PromotionCommandResponse>((await Mutate("end", draft.Id, version: end, key: key)).Body).Replayed);
        Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
        Assert.Equal(409, (await Mutate("update", draft.Id, new() { ProductId = product, Name = "Nie wolno", DiscountType = "amount", DiscountValue = 5, ExpectedVersion = 4 })).Status);
    }

    [PostgresFact]
    public async Task DatabaseTracksEffectiveVisibilityAndFuturePriceStartsOfferLater()
    {
        var productId = await SeedAsync();
        await using var db = Open();
        var variant = await db.ProductVariants.Where(v => v.ProductId == productId).FirstAsync();
        var category = await db.ProductCategories.AsNoTracking().Where(c => c.ProductId == productId).SingleAsync();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Hidden));
        await db.ProductCategories.Where(c => c.ProductId == productId).ExecuteDeleteAsync();
        Assert.False(await db.VariantOfferEvents.Where(e => e.VariantId == variant.Id).OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Select(e => e.Visible).FirstAsync());
        db.ProductCategories.Add(new() { ProductId = productId, CategoryId = category.CategoryId });
        await db.SaveChangesAsync();
        await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CatalogStatus.Published));
        await db.ProductVariants.Where(v => v.Id == variant.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Hidden));
        Assert.False(await db.VariantOfferEvents.Where(e => e.VariantId == variant.Id).OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Select(e => e.Visible).FirstAsync());
        await db.ProductVariants.Where(v => v.Id == variant.Id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, CatalogStatus.Published));
        Assert.True(await db.VariantOfferEvents.Where(e => e.VariantId == variant.Id).OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Select(e => e.Visible).FirstAsync());
        var future = DateTime.UtcNow.AddDays(1);
        var p = new Product { Name = "Nowy", Slug = "new-" + Guid.NewGuid().ToString("N") };
        var v = new ProductVariant { Product = p, SKU = "NEW-" + Guid.NewGuid().ToString("N") };
        db.ProductVariants.Add(v);
        db.ProductCategories.Add(new() { Product = p, CategoryId = category.CategoryId });
        db.VariantPriceChanges.Add(new() { Variant = v, BaseAmount = 100, SellingAmount = 100, EffectiveAt = future, Reason = "Przyszła" });
        await db.SaveChangesAsync();
        // Legacy offering with a future-only price remains readable; a new admin
        // publication is now rejected until a current PLN price exists.
        await LegacyCatalogFixture.ImportAsync(db, async () =>
        {
            await db.Products.Where(x => x.Id == p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CatalogStatus.Published));
            await db.ProductVariants.Where(x => x.Id == v.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CatalogStatus.Published));
        });
        var prices = await db.VariantPriceChanges.Where(c => c.VariantId == v.Id).ToArrayAsync();
        var events = await db.VariantOfferEvents.Where(e => e.VariantId == v.Id).ToArrayAsync();
        Assert.Null(PriorPriceCalculator.Calculate(prices, events, DateTime.UtcNow));
        Assert.Equal("SinceFirstOffer", PriorPriceCalculator.Calculate(prices, events, future.AddMinutes(1))!.Basis);
    }
}
