using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Shopping;

public sealed class ShoppingOfferReader(AppDbContext db)
{
    public async Task<Dictionary<Guid, ShoppingVariantResponse>> ReadAsync(Guid[] ids, DateTime now, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.Id) && v.Status == CatalogStatus.Published &&
                v.Product.Status == CatalogStatus.Published && v.Product.ProductCategories.Any())
            .Select(v => new
            {
                v.Id, v.ProductId, v.Product.Name, v.Product.Slug, v.SKU, v.ImageUrls,
                Quantity = v.Stock == null ? (int?)null : v.Stock.QuantityOnHand,
                Attributes = v.VariantAttributePairs.OrderBy(x => x.AttributePair.AttributeKey.Code)
                    .ThenBy(x => x.AttributePair.Value).ThenBy(x => x.AttributePairId)
                    .Select(x => new ProductVariantAttributeResponse(x.AttributePair.AttributeKey.Code,
                        x.AttributePair.AttributeKey.DisplayName, x.AttributePair.Value)).ToArray()
            }).ToListAsync(ct);
        var prices = await CurrentVariantPrices.Query(db, variants.Select(v => v.Id).ToArray(), now)
            .Where(p => p.Currency == "PLN").Include(p => p.PriceReduction).ToDictionaryAsync(p => p.VariantId, ct);
        return variants.Where(v => prices.ContainsKey(v.Id)).ToDictionary(v => v.Id, v =>
        {
            var p = prices[v.Id];
            return new ShoppingVariantResponse(v.Id, v.ProductId, v.Name, v.Slug, v.SKU,
                v.ImageUrls.FirstOrDefault(), v.Attributes, v.Quantity, p.Id,
                PublicPriceBuilder.Build(p.SellingAmount, p.BaseAmount, p.Currency, v.Id, p.PriceReduction));
        });
    }

    public static string Availability(ShoppingVariantResponse? variant, int quantity = 1) => variant switch
    {
        null => "unavailable",
        { QuantityOnHand: null } => "stock_unknown",
        { QuantityOnHand: <= 0 } => "out_of_stock",
        _ when variant.QuantityOnHand < quantity => "insufficient_stock",
        _ => "available"
    };
}
