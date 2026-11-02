using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog.Management;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class DynamicPromotionPostgresTests
{
    private static AppDbContext Open() => CatalogManagementPostgresTests.Open();
    private static readonly Guid Actor = CatalogManagementPostgresTests.Actor;
    private static async Task Seed(int products = 2)
    {
        await using var db = Open();
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(products, products), new(1, 1)), default);
    }
    private static async Task<PromotionResult> Change(string kind, Guid id, PromotionDraftRequest? draft = null, PromotionVersionRequest? version = null, Guid? key = null)
    {
        await using var db = Open();
        return await new PromotionService(db, TimeProvider.System).MutateAsync(kind, id, draft, version, key ?? Guid.NewGuid(), Actor, default);
    }
    private static async Task<PromotionResponse> Get(Guid id)
    {
        await using var db = Open();
        return Assert.IsType<PromotionResponse>((await new PromotionService(db, TimeProvider.System).GetAsync(id, default)).Body);
    }
    private static async Task<PromotionPreviewResponse> Preview(Guid id)
    {
        await using var db = Open();
        return Assert.IsType<PromotionPreviewResponse>((await new PromotionService(db, TimeProvider.System).PreviewAsync(id, 1, 100, default)).Body);
    }
    private static async Task<PromotionResponse> Draft(string scope = "catalog", Guid? category = null, decimal discount = 20, string type = "percentage", Guid[]? excludedProducts = null)
    {
        var result = await Change("create", Guid.Empty, new() { Scope = scope, CategoryId = category, Name = "Test dynamic", DiscountType = type,
            DiscountValue = discount, ExcludedProductIds = excludedProducts?.ToList() });
        Assert.Equal(201, result.Status);
        return Assert.IsType<PromotionCommandResponse>(result.Body).Promotion;
    }
    private static async Task<PromotionResult> Activate(Guid id, Guid? key = null)
    {
        var preview = await Preview(id);
        return await Change("activate", id, version: new PromotionActivationRequest { ExpectedVersion = preview.Version, ExpectedScopeToken = preview.ScopeToken }, key: key);
    }
    private static async Task<PromotionConflictPage> Conflicts(Guid? id = null, bool history = false)
    {
        await using var db = Open();
        return Assert.IsType<PromotionConflictPage>((await new DynamicPromotionService(db, TimeProvider.System).ConflictsAsync(id, history, 1, 100, default)).Body);
    }
    private static async Task Reconcile()
    {
        await using var db = Open(); await using var tx = await db.Database.BeginTransactionAsync();
        await CatalogWriteLock.AcquireAsync(db, default);
        await new DynamicPromotionEngine(db, TimeProvider.System).ReconcileAsync(default);
        await tx.CommitAsync();
    }

    [PostgresFact]
    public async Task PartialActivationKeepsInvalidPricesAndExposesConflictsAndPagedMembers()
    {
        await Seed(); var p = await Draft(discount: 60, type: "amount");
        var preview = await Preview(p.Id);
        Assert.True(preview.CanActivate); Assert.Equal(1, preview.EligibleVariants); Assert.Equal(2, preview.ConflictedVariants);
        Assert.Empty(preview.ExpectedPrices); Assert.Equal(64, preview.ScopeToken!.Length);
        Assert.Equal(200, (await Activate(p.Id)).Status);
        Assert.Equal(1, (await Get(p.Id)).ActiveVariants);
        Assert.Contains((await Conflicts(p.Id)).Items, c => c.Code == "invalid_discount");
        await using var db = Open();
        var cap = await db.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == p.Id && m.VariantId == cap.Id));
        var page = Assert.IsType<PromotionMemberPage>((await new DynamicPromotionService(db, TimeProvider.System).MembersAsync(p.Id, 1, 1, default)).Body);
        Assert.Single(page.Items); Assert.Equal(1, page.TotalItems);
        Assert.Empty((await Get(p.Id)).Variants); // New scopes use paged membership, not a huge nested response.
    }

    [PostgresFact]
    public async Task PriceFixAppliesAutomaticallyAndClosesConflictWithoutDuplicateDiscount()
    {
        await Seed(); var p = await Draft(discount: 60, type: "amount"); await Activate(p.Id);
        await using var db = Open();
        var cap = await db.ProductVariants.Where(v => v.SKU == "DEMO-CAP-ONE").Select(v => v.Id).SingleAsync();
        var current = await db.VariantPriceChanges.Where(c => c.VariantId == cap).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        // Price must drop below the actual earlier price too: 80 - 60 = 20 < 50.
        var result = await new VariantPriceService(db, TimeProvider.System).ChangeAsync(new(cap, 80, "PLN", "Naprawa", current.Id, Guid.NewGuid(), Actor));
        Assert.Equal(PriceChangeStatus.Applied, result.Status);
        var latest = await db.VariantPriceChanges.Where(c => c.VariantId == cap).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(20, latest.SellingAmount);
        Assert.DoesNotContain((await Conflicts(p.Id)).Items, c => c.VariantId == cap);
        Assert.Contains((await Conflicts(p.Id, true)).Items, c => c.VariantId == cap && c.ResolvedAt.HasValue);
        var count = await db.VariantPriceChanges.CountAsync();
        await Task.WhenAll(Reconcile(), Reconcile());
        Assert.Equal(count, await db.VariantPriceChanges.CountAsync());
    }

    [PostgresFact]
    public async Task ProductAndVariantExclusionsRestorePriceAndReplayOriginalResponse()
    {
        await Seed(); var p = await Draft(); await Activate(p.Id);
        await using var db = Open();
        var cap = await db.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        var shirt = await db.Products.SingleAsync(x => x.Slug == "demo-koszulka");
        var key = Guid.NewGuid();
        var request = new PromotionExclusionsRequest { ExpectedVersion = (await Get(p.Id)).Version, ProductIds = [cap.ProductId], VariantIds = [await db.ProductVariants.Where(v => v.SKU == "DEMO-TSHIRT-BLUE-M").Select(v => v.Id).SingleAsync()] };
        var result = await Change("exclusions", p.Id, version: request, key: key);
        Assert.Equal(200, result.Status); Assert.Equal(0, (await Get(p.Id)).ActiveVariants);
        var current = await db.VariantPriceChanges.Where(c => c.VariantId == cap.Id).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(current.BaseAmount, current.SellingAmount);
        Assert.True(Assert.IsType<PromotionCommandResponse>((await Change("exclusions", p.Id, version: request, key: key)).Body).Replayed);
        Assert.Equal(409, (await Change("exclusions", p.Id, version: request)).Status);
        var excluded = Assert.IsType<PromotionExclusionsResponse>((await new DynamicPromotionService(db, TimeProvider.System).ExclusionsAsync(p.Id, default)).Body);
        Assert.Contains(cap.ProductId, excluded.ProductIds); Assert.Single(excluded.VariantIds);
        Assert.Equal(200, (await Change("end", p.Id, version: new() { ExpectedVersion = (await Get(p.Id)).Version })).Status);
    }

    [PostgresFact]
    public async Task NewPublishedProductJoinsImmediatelyAndCategoryDepartureRestoresPrice()
    {
        await Seed(); await using var db = Open();
        var root = await db.Categories.SingleAsync(c => c.Slug == "demo-odziez");
        var child = await db.Categories.SingleAsync(c => c.Slug == "demo-koszulki");
        var p = await Draft("category", root.Id); await Activate(p.Id);
        var products = CatalogManagementPostgresTests.Products(db);
        var created = CatalogManagementPostgresTests.Receipt(await products.CreateAsync(new("Nowy", "dynamic-new", [child.Id], new("DYN-NEW", null, null, [], [])), Actor, Guid.NewGuid(), default), 201);
        var variantId = created.VariantId!.Value;
        var price = await new VariantPriceService(db, TimeProvider.System).ChangeAsync(new(variantId, 100, "PLN", "Pierwsza", null, Guid.NewGuid(), Actor));
        Assert.Equal(PriceChangeStatus.Applied, price.Status);
        var v = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == variantId);
        Assert.Equal(200, (await products.VariantStatusAsync(variantId, new("Published", v.Version), Actor, Guid.NewGuid(), default)).Status);
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(200, (await products.StatusAsync(product.Id, new("Published", product.Version), Actor, Guid.NewGuid(), default)).Status);
        Assert.True(await db.PromotionVariants.AnyAsync(m => m.PromotionId == p.Id && m.VariantId == variantId && m.EndedAt == null));
        var latest = await db.VariantPriceChanges.Where(c => c.VariantId == variantId).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(80, latest.SellingAmount);
        var other = new Category { DisplayName = "Inna", Slug = "other-root", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Categories.Add(other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(200, (await products.CategoriesAsync(product.Id, new([other.Id], product.Version), Actor, Guid.NewGuid(), default)).Status);
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == p.Id && m.VariantId == variantId && m.EndedAt == null));
        latest = await db.VariantPriceChanges.Where(c => c.VariantId == variantId).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(100, latest.SellingAmount);
    }

    [PostgresFact]
    public async Task OverlapBlocksNewActionAndRecordsDiagnosticsAndDatabaseGuardsRawWrites()
    {
        await Seed(); var first = await Draft(); await Activate(first.Id);
        var second = await Draft(discount: 30); var preview = await Preview(second.Id);
        Assert.False(preview.CanActivate); Assert.Equal(409, (await Activate(second.Id)).Status);
        Assert.NotEmpty((await Conflicts(second.Id)).Items);
        Assert.Equal("Draft", (await Get(second.Id)).Status);
        await using var db = Open();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.promotions SET status='Active',activated_at=clock_timestamp() WHERE id={second.Id}"));
        Assert.Equal("dynamic_promotion_overlap", error.ConstraintName);
        var exposed = await db.Database.SqlQuery<bool>($"SELECT has_table_privilege('anon','app.promotion_conflicts','SELECT') OR has_table_privilege('authenticated','app.promotion_exclusions','INSERT') OR has_table_privilege('service_role','app.active_promotion_claims','SELECT') AS \"Value\"").SingleAsync();
        Assert.False(exposed);
    }

    [PostgresFact]
    public async Task PreviewTokenDetectsCatalogChangeAndSuccessfulReplaySurvivesEnd()
    {
        await Seed(); var p = await Draft(); var preview = await Preview(p.Id);
        var request = new PromotionActivationRequest { ExpectedVersion = p.Version, ExpectedScopeToken = preview.ScopeToken };
        await using var db = Open();
        var cap = await db.ProductVariants.Where(v => v.SKU == "DEMO-CAP-ONE").Select(v => v.Id).SingleAsync();
        var current = await db.VariantPriceChanges.Where(c => c.VariantId == cap).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(PriceChangeStatus.Applied, (await new VariantPriceService(db, TimeProvider.System).ChangeAsync(new(cap, 40, "PLN", "Zmiana", current.Id, Guid.NewGuid(), Actor))).Status);
        Assert.Equal(409, (await Change("activate", p.Id, version: request)).Status);
        preview = await Preview(p.Id); request.ExpectedScopeToken = preview.ScopeToken;
        var key = Guid.NewGuid(); Assert.Equal(200, (await Change("activate", p.Id, version: request, key: key)).Status);
        await Change("end", p.Id, version: new() { ExpectedVersion = (await Get(p.Id)).Version });
        var replay = await Change("activate", p.Id, version: request, key: key);
        Assert.True(Assert.IsType<PromotionCommandResponse>(replay.Body).Replayed);
        Assert.Equal("Active", Assert.IsType<PromotionCommandResponse>(replay.Body).Promotion.Status);
        Assert.Equal("Ended", (await Get(p.Id)).Status);
    }

    [PostgresFact]
    public async Task WorkerReconcilesCommittedSqlChangesAndDoesNotRewriteHistory()
    {
        await Seed(); var p = await Draft(); await Activate(p.Id);
        await using var db = Open();
        var ids = await db.VariantPriceChanges.Select(c => c.Id).ToArrayAsync();
        var cap = await db.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.products SET status='Hidden' WHERE id={cap.ProductId}");
        Assert.True((await db.PromotionReconciliations.SingleAsync()).Dirty);
        await Task.WhenAll(RunWorker(), RunWorker());
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == p.Id && m.VariantId == cap.Id && m.EndedAt == null));
        Assert.Equal(ids.Length, await db.VariantPriceChanges.CountAsync(c => ids.Contains(c.Id)));
        Assert.False((await db.PromotionReconciliations.AsNoTracking().SingleAsync()).Dirty);
        static async Task RunWorker() { await using var context = Open(); await PromotionReconciliationWorker.RunOnceAsync(context, TimeProvider.System, default); }
    }

    [PostgresFact]
    public async Task CategoryAssignmentOverlapRollsBackAndLeavesDiagnosticForAdministrator()
    {
        await Seed(); await using var db = Open();
        var clothing = await db.Categories.Where(c => c.Slug == "demo-odziez").Select(c => c.Id).SingleAsync();
        var accessories = await db.Categories.Where(c => c.Slug == "demo-akcesoria").Select(c => c.Id).SingleAsync();
        var a = await Draft("category", clothing); var b = await Draft("category", accessories);
        Assert.Equal(200, (await Activate(a.Id)).Status); Assert.Equal(200, (await Activate(b.Id)).Status);
        var cap = await db.Products.AsNoTracking().SingleAsync(p => p.Slug == "demo-czapka");
        var result = await CatalogManagementPostgresTests.Products(db).CategoriesAsync(cap.Id, new([clothing, accessories], cap.Version), Actor, Guid.NewGuid(), default);
        Assert.Equal(409, result.Status); Assert.Equal("active_promotion", Assert.IsType<CatalogError>(result.Body).Code);
        Assert.Equal([accessories], await db.ProductCategories.Where(c => c.ProductId == cap.Id).Select(c => c.CategoryId).ToArrayAsync());
        Assert.Contains((await Conflicts()).Items, c => c.Code == "active_promotion" && c.OtherPromotionId.HasValue);
    }

    [PostgresFact]
    public async Task ConcurrentStartsOnlyApplyOneCampaignAndFailedCommitRollsBackEveryPrice()
    {
        await Seed(); var first = await Draft(); var second = await Draft(discount: 30);
        var results = await Task.WhenAll(Activate(first.Id), Activate(second.Id));
        Assert.Single(results, r => r.Status == 200); Assert.Single(results, r => r.Status == 409);
        await Seed(); var p = await Draft();
        await using var db = Open();
        var prices = await db.VariantPriceChanges.CountAsync();
        var operations = await db.PromotionOperations.CountAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION app.test_dynamic_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $$
              BEGIN RAISE EXCEPTION 'Intentional commit failure'; END $$;
            CREATE CONSTRAINT TRIGGER test_dynamic_commit_failure AFTER INSERT ON app.promotion_variants
              DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.test_dynamic_commit_failure();
            """);
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => Activate(p.Id));
            Assert.Equal(prices, await db.VariantPriceChanges.CountAsync());
            Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == p.Id));
            Assert.False(await db.PriceReductions.AnyAsync());
            Assert.Equal(operations, await db.PromotionOperations.CountAsync());
            Assert.Equal("Draft", (await Get(p.Id)).Status);
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER test_dynamic_commit_failure ON app.promotion_variants; DROP FUNCTION app.test_dynamic_commit_failure()"); }
    }

    [PostgresFact]
    public async Task CampaignWithOnlyInvalidDiscountsCanRemainActiveAndEndWithoutMembers()
    {
        await Seed(); var p = await Draft(discount: 200, type: "amount");
        Assert.Equal(200, (await Activate(p.Id)).Status);
        Assert.Equal("Active", (await Get(p.Id)).Status); Assert.Equal(0, (await Get(p.Id)).ActiveVariants);
        Assert.NotEmpty((await Conflicts(p.Id)).Items);
        Assert.Equal(200, (await Change("end", p.Id, version: new() { ExpectedVersion = (await Get(p.Id)).Version })).Status);
        Assert.Empty((await Conflicts(p.Id)).Items);
    }

    [PostgresFact]
    public async Task ReentryCreatesNewPeriodWithFreshReferenceAndKeepsPreviousHistory()
    {
        await Seed(); var p = await Draft(); await Activate(p.Id);
        await using var db = Open();
        var cap = await db.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        var product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == cap.ProductId);
        var service = CatalogManagementPostgresTests.Products(db);
        Assert.Equal(200, (await service.StatusAsync(product.Id, new("Hidden", product.Version), Actor, Guid.NewGuid(), default)).Status);
        var current = await db.VariantPriceChanges.Where(c => c.VariantId == cap.Id).OrderByDescending(c => c.EffectiveAt).FirstAsync();
        Assert.Equal(PriceChangeStatus.Applied, (await new VariantPriceService(db, TimeProvider.System).ChangeAsync(new(cap.Id, 40, "PLN", "Nowa podstawa", current.Id, Guid.NewGuid(), Actor))).Status);
        product = await db.Products.AsNoTracking().SingleAsync(x => x.Id == cap.ProductId);
        Assert.Equal(200, (await service.StatusAsync(product.Id, new("Published", product.Version), Actor, Guid.NewGuid(), default)).Status);
        var periods = await db.PromotionVariants.AsNoTracking().Where(m => m.PromotionId == p.Id && m.VariantId == cap.Id).OrderBy(m => m.StartedAt).ToArrayAsync();
        Assert.Equal(2, periods.Length); Assert.NotNull(periods[0].EndedAt); Assert.Null(periods[1].EndedAt);
        var references = await db.PriceReductions.AsNoTracking().Where(r => r.PromotionVariantId == periods[0].Id || r.PromotionVariantId == periods[1].Id).OrderBy(r => r.StartedAt).ToArrayAsync();
        Assert.Equal(50, references[0].PriorAmount); Assert.Equal(40, references[1].PriorAmount);
        Assert.True(references[1].StartedAt > references[0].StartedAt);
        Assert.Equal(32, await db.VariantPriceChanges.Where(c => c.VariantId == cap.Id).OrderByDescending(c => c.EffectiveAt).Select(c => c.SellingAmount).FirstAsync());
    }

    [PostgresFact]
    public async Task MovingSubtreeTransfersDiscountBetweenDisjointCategoryPromotions()
    {
        await Seed(); await using var db = Open();
        var clothing = await db.Categories.SingleAsync(c => c.Slug == "demo-odziez");
        var accessories = await db.Categories.SingleAsync(c => c.Slug == "demo-akcesoria");
        var child = await db.Categories.SingleAsync(c => c.Slug == "demo-koszulki");
        var a = await Draft("category", clothing.Id); var b = await Draft("category", accessories.Id, 50);
        await Activate(a.Id); await Activate(b.Id);
        var moved = await CatalogManagementPostgresTests.Categories(db).ParentAsync(child.Id, new(accessories.Id, child.Version), Actor, Guid.NewGuid(), default);
        Assert.Equal(200, moved.Status);
        var blue = await db.ProductVariants.Where(v => v.SKU == "DEMO-TSHIRT-BLUE-M").Select(v => v.Id).SingleAsync();
        Assert.False(await db.PromotionVariants.AnyAsync(m => m.PromotionId == a.Id && m.VariantId == blue && m.EndedAt == null));
        Assert.True(await db.PromotionVariants.AnyAsync(m => m.PromotionId == b.Id && m.VariantId == blue && m.EndedAt == null));
        Assert.Equal(55, await db.VariantPriceChanges.Where(c => c.VariantId == blue).OrderByDescending(c => c.EffectiveAt).Select(c => c.SellingAmount).FirstAsync());
    }
}
