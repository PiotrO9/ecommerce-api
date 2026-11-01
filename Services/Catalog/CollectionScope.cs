using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog;

public static class CollectionScope
{
    public static bool ValidSelector(Guid? id, string? slug, bool required = false) =>
        id != Guid.Empty && !(id.HasValue && slug is not null) && (slug is null || !string.IsNullOrWhiteSpace(slug))
        && (!required || id.HasValue || slug is not null);

    public static Task<Collection?> ResolveAsync(AppDbContext db, Guid? id, string? slug, CancellationToken ct) =>
        db.Collections.AsNoTracking().SingleOrDefaultAsync(c => c.Status == "Published" &&
            (id.HasValue ? c.Id == id : c.Slug == slug!.Trim().ToLowerInvariant()), ct);

    public static IQueryable<Product> Products(AppDbContext db, Guid id) => db.Products.Where(p =>
        db.CollectionProducts.Any(l => l.CollectionId == id && l.ProductId == p.Id));

    public static IQueryable<Product> VisibleProducts(AppDbContext db, DateTime now) => db.Products.Where(p =>
        p.Status == CatalogStatus.Published && p.ProductCategories.Any() &&
        p.Variants.Any(v => v.Status == CatalogStatus.Published && v.PriceChanges.Any(c => c.EffectiveAt <= now)));
}
