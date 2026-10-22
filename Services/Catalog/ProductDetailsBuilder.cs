using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Entities;

namespace E_commerce_backend.Services.Catalog;

public static class ProductDetailsBuilder
{
    public static ProductDetailsResponse? Build(Product? product, DateTime now)
    {
        if (product is null || product.Status != CatalogStatus.Published)
        {
            return null;
        }

        var variants = product.Variants
            .Where(variant => variant.Status == CatalogStatus.Published)
            .OrderBy(variant => variant.SKU, StringComparer.Ordinal)
            .Select(variant => BuildVariant(variant, now))
            .OfType<ProductVariantResponse>()
            .ToArray();

        if (variants.Length == 0)
        {
            return null;
        }

        var categories = product.ProductCategories
            .Select(link => link.Category)
            .OrderBy(category => category.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Id)
            .Select(category => new ProductCategoryResponse(
                category.Id, category.DisplayName, category.Slug))
            .ToArray();

        if (categories.Length == 0)
        {
            return null;
        }

        return new ProductDetailsResponse(
            product.Id, product.Name, product.Slug,
            product.CreatedAt, product.UpdatedAt, product.PublishedAt,
            categories, variants);
    }

    private static ProductVariantResponse? BuildVariant(ProductVariant variant, DateTime now)
    {
        var currentPrices = variant.PriceChanges
            .Where(price => price.EffectiveAt <= now)
            .GroupBy(price => price.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(price => price.EffectiveAt)
                .ThenByDescending(price => price.CreatedAt)
                .ThenByDescending(price => price.Id)
                .First())
            .Select(price => new ProductVariantPriceResponse(
                price.SellingAmount, price.Currency))
            .ToArray();

        if (currentPrices.Length == 0)
        {
            return null;
        }

        var attributes = variant.VariantAttributePairs
            .Select(link => link.AttributePair)
            .OrderBy(pair => pair.AttributeKey.Code, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.Id)
            .Select(pair => new ProductVariantAttributeResponse(
                pair.AttributeKey.Code, pair.AttributeKey.DisplayName, pair.Value))
            .ToArray();

        return new ProductVariantResponse(
            variant.Id, variant.SKU, variant.Description, variant.ShortDescription,
            variant.ImageUrls, variant.CreatedAt, variant.UpdatedAt, variant.PublishedAt,
            attributes, variant.Stock?.QuantityOnHand, currentPrices);
    }
}
