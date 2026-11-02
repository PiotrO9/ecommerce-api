using System.Data;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed class CatalogManagementReader(AppDbContext db, TimeProvider clock)
{
    public async Task<CatalogResult> SnapshotAsync(Func<Task<CatalogResult>> read, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        return await read();
    }
    public static ProductListPaginationResponse Pagination(int page, int size, int total) =>
        new(page, size, total, (int)Math.Ceiling((double)total / size), page > 1, (long)page * size < total);
    private static void Page(int page, int size)
    {
        if (!CollectionService.ValidPage(page, size)) throw new CatalogValidation("invalid_pagination");
    }
    public static AdminVariantRow VariantRow(ProductVariant v) => new(v.Id, v.ProductId, v.SKU, v.Status.ToString(), v.Version,
        v.Description, v.ShortDescription, v.ImageUrls, v.CreatedAt, v.UpdatedAt, v.PublishedAt);
    public static AdminStock? StockRow(VariantStock? s, int reserved = 0) => s is null ? null : new(s.Id, s.VariantId, s.QuantityOnHand, s.Version, s.CreatedAt, s.UpdatedAt, reserved);
    public static AdminCategory CategoryRow(Category c) => new(c.Id, c.DisplayName, c.Slug, c.ParentId, c.Version, c.CreatedAt, c.UpdatedAt);

    public async Task<CatalogResult> ProductsAsync(AdminProductSearchRequest? r, CancellationToken ct)
    {
        if (r is null) throw new CatalogValidation("body_required");
        Page(r.Page, r.PageSize);
        var q = CatalogInput.Text(r.Q, 100, "q", false);
        if (r.Status is not (null or "Draft" or "Published" or "Hidden")) throw new CatalogValidation("invalid_status");
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.Products.AsNoTracking();
        if (r.Status is not null) { var status = Enum.Parse<CatalogStatus>(r.Status); query = query.Where(p => p.Status == status); }
        if (q is not null)
        {
            var pattern = "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern, "\\") || p.Variants.Any(v => EF.Functions.ILike(v.SKU, pattern, "\\")));
        }
        if (r.CategoryId is Guid category)
        {
            var categories = await db.Categories.AsNoTracking().ToArrayAsync(ct);
            var branch = CategoryTreeBuilder.GetBranchIds(categories, category) ?? throw new CatalogValidation("category_not_found", status: 404);
            var ids = branch.ToArray(); query = query.Where(p => p.ProductCategories.Any(c => ids.Contains(c.CategoryId)));
        }
        if (r.CollectionId is Guid collection)
        {
            if (!await db.Collections.AnyAsync(c => c.Id == collection, ct)) throw new CatalogValidation("collection_not_found", status: 404);
            query = query.Where(p => db.CollectionProducts.Any(c => c.CollectionId == collection && c.ProductId == p.Id));
        }
        if (r.MissingPrice is bool missingPrice)
            query = query.Where(p => p.Variants.Any(v => !v.PriceChanges.Any(price => price.Currency == "PLN" && price.EffectiveAt <= now)) == missingPrice);
        if (r.MissingStock is bool missingStock) query = query.Where(p => p.Variants.Any(v => v.Stock == null) == missingStock);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((r.Page - 1) * r.PageSize).Take(r.PageSize)
            .Select(p => new AdminProductRow(p.Id, p.Name, p.Slug, p.Status.ToString(), p.Version, p.Variants.Count,
                p.CreatedAt, p.UpdatedAt, p.PublishedAt)).ToArrayAsync(ct);
        return new(200, new AdminPage<AdminProductRow>(rows, Pagination(r.Page, r.PageSize, total)));
    }
    public async Task<CatalogResult> ProductAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => new AdminProductRow(p.Id, p.Name, p.Slug,
            p.Status.ToString(), p.Version, p.Variants.Count, p.CreatedAt, p.UpdatedAt, p.PublishedAt)).SingleOrDefaultAsync(ct);
        CatalogCommandStore.Found(row, "product");
        var categories = await db.ProductCategories.Where(c => c.ProductId == id).OrderBy(c => c.CategoryId).Select(c => c.CategoryId).ToArrayAsync(ct);
        return new(200, new AdminProductDetails(row!, categories));
    }
    public async Task<CatalogResult> VariantsAsync(Guid productId, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw new CatalogValidation("product_not_found", status: 404);
        var query = db.ProductVariants.AsNoTracking().Where(v => v.ProductId == productId);
        var count = await query.CountAsync(ct);
        var variants = await query.OrderBy(v => v.SKU).ThenBy(v => v.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var ids = variants.Select(v => v.Id).ToArray();
        var now = clock.GetUtcNow().UtcDateTime;
        var prices = await CurrentVariantPrices.Query(db, ids, now).Where(p => p.Currency == "PLN").ToDictionaryAsync(p => p.VariantId, ct);
        var reserved = await E_commerce_backend.Services.Inventory.InventoryAvailability.ReservedAsync(db, ids, now, ct);
        var stocks = await db.VariantStocks.AsNoTracking().Where(s => ids.Contains(s.VariantId)).ToDictionaryAsync(s => s.VariantId, ct);
        var items = variants.Select(v => new AdminVariantSummary(VariantRow(v), prices.GetValueOrDefault(v.Id)?.SellingAmount,
            "PLN", StockRow(stocks.GetValueOrDefault(v.Id), reserved.GetValueOrDefault(v.Id)))).ToArray();
        return new(200, new AdminPage<AdminVariantSummary>(items, Pagination(page, size, count)));
    }
    public async Task<CatalogResult> VariantAsync(Guid id, CancellationToken ct)
    {
        var v = CatalogCommandStore.Found(await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct), "variant");
        var version = await db.Products.Where(p => p.Id == v.ProductId).Select(p => p.Version).SingleAsync(ct);
        var pairs = await db.VariantAttributePairs.Where(l => l.VariantId == id).OrderBy(l => l.AttributePairId).Select(l => l.AttributePairId).ToArrayAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var price = await CurrentVariantPrices.Query(db, [id], now).Where(p => p.Currency == "PLN").SingleOrDefaultAsync(ct);
        var reserved = await E_commerce_backend.Services.Inventory.InventoryAvailability.ReservedAsync(db, [id], now, ct);
        var stock = await db.VariantStocks.AsNoTracking().SingleOrDefaultAsync(s => s.VariantId == id, ct);
        return new(200, new AdminVariantDetails(VariantRow(v), version, pairs, price?.SellingAmount, "PLN", StockRow(stock, reserved.GetValueOrDefault(id))));
    }
    public async Task<CatalogResult> StockAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ProductVariants.AnyAsync(v => v.Id == id, ct)) throw new CatalogValidation("variant_not_found", status: 404);
        var reserved = await E_commerce_backend.Services.Inventory.InventoryAvailability.ReservedAsync(db, [id], clock.GetUtcNow().UtcDateTime, ct);
        return new(200, new AdminStockResponse(id, StockRow(await db.VariantStocks.AsNoTracking().SingleOrDefaultAsync(s => s.VariantId == id, ct), reserved.GetValueOrDefault(id))));
    }
    public async Task<CatalogResult> CategoriesAsync(Guid? id, CancellationToken ct)
    {
        if (id.HasValue) return new(200, CategoryRow(CatalogCommandStore.Found(await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct), "category")));
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.DisplayName).ThenBy(c => c.Id).ToArrayAsync(ct);
        var nodes = categories.ToDictionary(c => c.Id, c => new AdminCategoryNode(CategoryRow(c), []));
        var roots = new List<AdminCategoryNode>();
        foreach (var c in categories) { if (c.ParentId is Guid parent) nodes[parent].Children.Add(nodes[c.Id]); else roots.Add(nodes[c.Id]); }
        return new(200, roots);
    }
    public async Task<CatalogResult> KeysAsync(int page, int size, CancellationToken ct)
    {
        Page(page, size);
        var count = await db.AttributeKeys.CountAsync(ct);
        var items = await db.AttributeKeys.AsNoTracking().OrderBy(k => k.Code).ThenBy(k => k.Id).Skip((page - 1) * size).Take(size)
            .Select(k => new AdminAttributeKey(k.Id, k.Code, k.DisplayName, k.Version)).ToArrayAsync(ct);
        return new(200, new AdminPage<AdminAttributeKey>(items, Pagination(page, size, count)));
    }
    public async Task<CatalogResult> PairsAsync(Guid id, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        if (!await db.AttributeKeys.AnyAsync(k => k.Id == id, ct)) throw new CatalogValidation("attribute_key_not_found", status: 404);
        var query = db.AttributePairs.AsNoTracking().Where(p => p.AttributeKeyId == id);
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Value).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new AdminAttributePair(p.Id, p.AttributeKeyId, p.Value, p.CreatedAt, p.UpdatedAt)).ToArrayAsync(ct);
        return new(200, new AdminPage<AdminAttributePair>(items, Pagination(page, size, count)));
    }
    public async Task<CatalogResult> ReadinessAsync(Guid id, bool variant, int page, int size, CancellationToken ct)
    {
        Page(page, size);
        var productId = variant ? CatalogCommandStore.Found(await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct), "variant").ProductId : id;
        var p = CatalogCommandStore.Found(await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, ct), "product");
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.ProductVariants.AsNoTracking().Where(v => v.ProductId == productId && (!variant || v.Id == id));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(v => v.SKU).ThenBy(v => v.Id).Skip((page - 1) * size).Take(size)
            .Select(v => new { v.Id, v.Version, HasPrice = v.PriceChanges.Any(c => c.Currency == "PLN" && c.EffectiveAt <= now),
                HasStock = v.Stock != null, Quantity = v.Stock == null ? 0 : v.Stock.QuantityOnHand, HasImage = v.ImageUrls.Length > 0,
                HasDescription = v.Description != null && v.Description != "" }).ToArrayAsync(ct);
        var items = rows.Select(v =>
        {
            var blockers = new List<ReadinessIssue>(); var warnings = new List<ReadinessIssue>();
            if (!v.HasPrice) blockers.Add(new("missing_current_pln_price", "price", v.Id));
            if (!v.HasStock) warnings.Add(new("unknown_stock", "stock", v.Id));
            else if (v.Quantity == 0) warnings.Add(new("out_of_stock", "stock", v.Id));
            if (!v.HasImage) warnings.Add(new("missing_image", "imageUrls", v.Id));
            if (!v.HasDescription) warnings.Add(new("missing_description", "description", v.Id));
            return new VariantReadiness(v.Id, v.Version, blockers.Count == 0, blockers, warnings);
        }).ToArray();
        if (variant) return new(200, items.Single());
        var productBlockers = new List<ReadinessIssue>();
        if (!await db.ProductCategories.AnyAsync(c => c.ProductId == id, ct)) productBlockers.Add(new("missing_category", "categoryIds"));
        if (!await query.AnyAsync(v => v.Status == CatalogStatus.Published, ct)) productBlockers.Add(new("no_published_variant", "variants"));
        if (await query.AnyAsync(v => v.Status == CatalogStatus.Published && !v.PriceChanges.Any(c => c.Currency == "PLN" && c.EffectiveAt <= now), ct))
            productBlockers.Add(new("published_variant_missing_price", "variants"));
        var productWarnings = new List<ReadinessIssue>();
        if (await query.AnyAsync(v => v.Stock == null, ct)) productWarnings.Add(new("unknown_stock", "variants"));
        if (await query.AnyAsync(v => v.Stock != null && v.Stock.QuantityOnHand == 0, ct)) productWarnings.Add(new("out_of_stock", "variants"));
        return new(200, new PublicationReadiness(id, p.Version, productBlockers.Count == 0, productBlockers, productWarnings,
            new(items, Pagination(page, size, total))));
    }
}
