using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed class ProductManagementService(AppDbContext db, CatalogCommandStore store, TimeProvider clock)
{
    public Task<CatalogResult> CreateAsync(ProductCreateRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var name = CatalogInput.Text(r.Name, 200, "name")!;
        var slug = CatalogInput.Slug(r.Slug, name);
        var categories = CatalogInput.Ids(r.CategoryIds, "categoryIds", 1000, true);
        var draft = CatalogInput.Variant(r.FirstVariant);
        return store.ExecuteAsync("product.create", Guid.Empty, null, new { name, slug, categories, draft }, actor, key, async () =>
        {
            await CategoriesExist(categories, ct); await PairsExist(draft.AttributePairIds!, ct); await AvailableSku(draft.Sku!, null, ct);
            await AvailableSlug(slug, null, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var product = new Product { Name = name, Slug = slug, CreatedAt = now, UpdatedAt = now };
            var variant = NewVariant(product.Id, draft, now);
            db.Products.Add(product); db.ProductVariants.Add(variant);
            db.ProductCategories.AddRange(categories.Select(c => new ProductCategory { ProductId = product.Id, CategoryId = c }));
            db.VariantAttributePairs.AddRange(draft.AttributePairIds!.Select(p => new VariantAttributePair { VariantId = variant.Id, AttributePairId = p }));
            await db.SaveChangesAsync(ct);
            return new(await ProductResponse(product.Id, variant.Id, ct), 201);
        }, ct);
    }
    public Task<CatalogResult> EditAsync(Guid id, ProductEditRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        if (r.Slug is null) throw new CatalogValidation("slug_required", "slug");
        var name = CatalogInput.Text(r.Name, 200, "name")!; var slug = CatalogInput.Slug(r.Slug, name);
        return store.ExecuteAsync("product.update", id, r.ExpectedVersion, new { name, slug }, actor, key, async () =>
        {
            var p = await Product(id, r.ExpectedVersion, ct); await AvailableSlug(slug, id, ct);
            await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.Slug, slug), ct);
            return new(await ProductResponse(id, null, ct));
        }, ct);
    }
    public Task<CatalogResult> CategoriesAsync(Guid id, CategoryIdsRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var ids = CatalogInput.Ids(r.CategoryIds, "categoryIds", 1000);
        return store.ExecuteAsync("product.categories", id, r.ExpectedVersion, ids, actor, key, async () =>
        {
            var p = await Product(id, r.ExpectedVersion, ct); await CategoriesExist(ids, ct);
            if (p.Status == CatalogStatus.Published && ids.Length == 0) throw new CatalogValidation("missing_category", status: 409);
            var old = await db.ProductCategories.Where(l => l.ProductId == id).Select(l => l.CategoryId).ToArrayAsync(ct);
            await db.ProductCategories.Where(l => l.ProductId == id && !ids.Contains(l.CategoryId)).ExecuteDeleteAsync(ct);
            db.ProductCategories.AddRange(ids.Except(old).Select(c => new ProductCategory { ProductId = id, CategoryId = c }));
            await db.SaveChangesAsync(ct);
            return new(await ProductResponse(id, null, ct));
        }, ct);
    }
    public Task<CatalogResult> StatusAsync(Guid id, StatusRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var status = ParseStatus(r.Status);
        return store.ExecuteAsync("product.status", id, r.ExpectedVersion, status.ToString(), actor, key, async () =>
        {
            var p = await Product(id, r.ExpectedVersion, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (status == CatalogStatus.Published)
            {
                if (!await db.ProductCategories.AnyAsync(c => c.ProductId == id, ct)) throw new CatalogValidation("missing_category", status: 409);
                var published = db.ProductVariants.Where(v => v.ProductId == id && v.Status == CatalogStatus.Published);
                if (!await published.AnyAsync(ct)) throw new CatalogValidation("no_published_variant", status: 409);
                if (await published.AnyAsync(v => !v.PriceChanges.Any(c => c.Currency == "PLN" && c.EffectiveAt <= now), ct))
                    throw new CatalogValidation("published_variant_missing_price", status: 409);
            }
            var date = p.PublishedAt ?? (status == CatalogStatus.Published ? now : (DateTime?)null);
            await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status).SetProperty(p => p.PublishedAt, date), ct);
            return new(await ProductResponse(id, null, ct));
        }, ct);
    }
    public Task<CatalogResult> AddVariantAsync(Guid id, VariantCreateRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var draft = CatalogInput.Variant(new(r.Sku, r.Description, r.ShortDescription, r.ImageUrls, r.AttributePairIds));
        return store.ExecuteAsync("variant.create", id, r.ExpectedProductVersion, draft, actor, key, async () =>
        {
            await Product(id, r.ExpectedProductVersion, ct); await PairsExist(draft.AttributePairIds!, ct);
            await AvailableSku(draft.Sku!, null, ct); await UniqueConfiguration(id, null, draft.AttributePairIds!, ct);
            var variant = NewVariant(id, draft, clock.GetUtcNow().UtcDateTime);
            db.ProductVariants.Add(variant);
            db.VariantAttributePairs.AddRange(draft.AttributePairIds!.Select(p => new VariantAttributePair { VariantId = variant.Id, AttributePairId = p }));
            await db.SaveChangesAsync(ct);
            return new(await VariantResponse(variant.Id, ct), 201);
        }, ct);
    }
    public Task<CatalogResult> EditVariantAsync(Guid id, VariantEditRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var draft = CatalogInput.Variant(new(r.Sku, r.Description, r.ShortDescription, r.ImageUrls, []));
        return store.ExecuteAsync("variant.update", id, r.ExpectedVersion, draft, actor, key, async () =>
        {
            await Variant(id, r.ExpectedVersion, ct); await AvailableSku(draft.Sku!, id, ct);
            await db.ProductVariants.Where(v => v.Id == id).ExecuteUpdateAsync(s => s.SetProperty(v => v.SKU, draft.Sku!)
                .SetProperty(v => v.Description, draft.Description).SetProperty(v => v.ShortDescription, draft.ShortDescription)
                .SetProperty(v => v.ImageUrls, draft.ImageUrls!), ct);
            return new(await VariantResponse(id, ct));
        }, ct);
    }
    public Task<CatalogResult> AttributesAsync(Guid id, AttributeIdsRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var ids = CatalogInput.Ids(r.AttributePairIds, "attributePairIds", 100);
        return store.ExecuteAsync("variant.attributes", id, r.ExpectedVersion, ids, actor, key, async () =>
        {
            var v = await Variant(id, r.ExpectedVersion, ct); await PairsExist(ids, ct);
            var old = await db.VariantAttributePairs.Where(l => l.VariantId == id).Select(l => l.AttributePairId).ToArrayAsync(ct);
            if (!old.Order().SequenceEqual(ids))
            {
                if (v.Status != CatalogStatus.Draft || v.PublishedAt != null || await db.VariantPriceChanges.AnyAsync(p => p.VariantId == id, ct) ||
                    await db.PromotionVariants.AnyAsync(p => p.VariantId == id, ct) ||
                    await db.VariantOfferEvents.AnyAsync(e => e.VariantId == id && e.Visible, ct))
                    throw new CatalogValidation("variant_configuration_locked", status: 409);
                await UniqueConfiguration(v.ProductId, id, ids, ct);
                await db.VariantAttributePairs.Where(l => l.VariantId == id && !ids.Contains(l.AttributePairId)).ExecuteDeleteAsync(ct);
                db.VariantAttributePairs.AddRange(ids.Except(old).Select(p => new VariantAttributePair { VariantId = id, AttributePairId = p }));
                await db.SaveChangesAsync(ct);
            }
            return new(await VariantResponse(id, ct));
        }, ct);
    }
    public Task<CatalogResult> VariantStatusAsync(Guid id, StatusRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var status = ParseStatus(r.Status);
        return store.ExecuteAsync("variant.status", id, r.ExpectedVersion, status.ToString(), actor, key, async () =>
        {
            var v = await Variant(id, r.ExpectedVersion, ct); var now = clock.GetUtcNow().UtcDateTime;
            if (status == CatalogStatus.Published && !await db.VariantPriceChanges.AnyAsync(p => p.VariantId == id && p.Currency == "PLN" && p.EffectiveAt <= now, ct))
                throw new CatalogValidation("missing_current_pln_price", status: 409);
            if (status != CatalogStatus.Published && await db.Products.AnyAsync(p => p.Id == v.ProductId && p.Status == CatalogStatus.Published, ct) &&
                !await db.ProductVariants.AnyAsync(other => other.ProductId == v.ProductId && other.Id != id && other.Status == CatalogStatus.Published, ct))
                throw new CatalogValidation("no_published_variant", status: 409);
            var date = v.PublishedAt ?? (status == CatalogStatus.Published ? now : (DateTime?)null);
            await db.ProductVariants.Where(v => v.Id == id).ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, status).SetProperty(v => v.PublishedAt, date), ct);
            return new(await VariantResponse(id, ct));
        }, ct);
    }
    private async Task<Product> Product(Guid id, long version, CancellationToken ct)
    {
        var p = CatalogCommandStore.Found(await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct), "product");
        CatalogCommandStore.Version(p.Version, version); return p;
    }
    private async Task<ProductVariant> Variant(Guid id, long version, CancellationToken ct)
    {
        var v = CatalogCommandStore.Found(await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct), "variant");
        CatalogCommandStore.Version(v.Version, version); return v;
    }
    private static CatalogStatus ParseStatus(string? status) => status switch
    {
        "Draft" => CatalogStatus.Draft, "Published" => CatalogStatus.Published, "Hidden" => CatalogStatus.Hidden,
        _ => throw new CatalogValidation("invalid_status", "status")
    };
    private async Task CategoriesExist(Guid[] ids, CancellationToken ct)
    { if (await db.Categories.CountAsync(c => ids.Contains(c.Id), ct) != ids.Length) throw new CatalogValidation("category_not_found", status: 404); }
    private async Task PairsExist(Guid[] ids, CancellationToken ct)
    { if (await db.AttributePairs.CountAsync(p => ids.Contains(p.Id), ct) != ids.Length) throw new CatalogValidation("attribute_pair_not_found", status: 404); }
    private async Task AvailableSku(string sku, Guid? id, CancellationToken ct)
    { if (await db.ProductVariants.AnyAsync(v => v.SKU == sku && v.Id != id, ct)) throw new CatalogValidation("sku_conflict", "sku", 409); }
    private async Task AvailableSlug(string slug, Guid? id, CancellationToken ct)
    { if (await db.Products.AnyAsync(p => p.Slug == slug && p.Id != id, ct)) throw new CatalogValidation("slug_conflict", "slug", 409); }
    private async Task UniqueConfiguration(Guid productId, Guid? id, Guid[] ids, CancellationToken ct)
    {
        if (await db.ProductVariants.AnyAsync(v => v.ProductId == productId && v.Id != id &&
            v.VariantAttributePairs.Count == ids.Length && v.VariantAttributePairs.All(l => ids.Contains(l.AttributePairId)), ct))
            throw new CatalogValidation("duplicate_variant_configuration", "attributePairIds", 409);
    }
    private static ProductVariant NewVariant(Guid productId, VariantDraftRequest r, DateTime now) => new()
    { ProductId = productId, SKU = r.Sku!, Description = r.Description, ShortDescription = r.ShortDescription, ImageUrls = r.ImageUrls!, CreatedAt = now, UpdatedAt = now };
    private async Task<CatalogCommandResponse> ProductResponse(Guid id, Guid? variantId, CancellationToken ct)
    {
        var version = await db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync(ct);
        var variantVersion = variantId is Guid vid ? await db.ProductVariants.Where(v => v.Id == vid).Select(v => (long?)v.Version).SingleAsync(ct) : null;
        return new(false, id, version, id, version, variantId, variantVersion);
    }
    private async Task<CatalogCommandResponse> VariantResponse(Guid id, CancellationToken ct)
    {
        var v = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == id, ct);
        var version = await db.Products.Where(p => p.Id == v.ProductId).Select(p => p.Version).SingleAsync(ct);
        return new(false, id, v.Version, v.ProductId, version, id, v.Version);
    }
}
