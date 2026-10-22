using E_commerce_backend.Entities;
using E_commerce_backend.Services.Catalog;

namespace E_commerce_backend.Tests.Unit;

public sealed class ProductDetailsBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PublishedProductShowsCurrentPricesAttributesAndKnownStock()
    {
        var product = NewProduct();
        var category = new Category { DisplayName = "Koszulki", Slug = "koszulki" };
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id, Product = product,
            CategoryId = category.Id, Category = category
        });

        var variant = NewVariant(product, "SHIRT-RED-S", CatalogStatus.Published);
        variant.ImageUrls = ["https://example.test/red-shirt.jpg"];
        variant.Stock = new VariantStock { VariantId = variant.Id, QuantityOnHand = 3 };
        variant.PriceChanges.Add(NewPrice(variant, "PLN", 100, Now.AddDays(-20), Now.AddDays(-20)));
        variant.PriceChanges.Add(NewPrice(variant, "PLN", 90, Now.AddDays(-2), Now.AddDays(-2)));
        variant.PriceChanges.Add(NewPrice(variant, "PLN", 85, Now.AddDays(-2), Now.AddDays(-1)));
        variant.PriceChanges.Add(NewPrice(variant, "PLN", 80, Now.AddDays(1), Now));
        variant.PriceChanges.Add(NewPrice(variant, "EUR", 20, Now.AddDays(-3), Now.AddDays(-3)));

        var key = new AttributeKey { Code = "kolor", DisplayName = "Kolor" };
        var pair = new AttributePair { AttributeKeyId = key.Id, AttributeKey = key, Value = "czerwony" };
        variant.VariantAttributePairs.Add(new VariantAttributePair
        {
            VariantId = variant.Id, AttributePairId = pair.Id, AttributePair = pair
        });
        product.Variants.Add(variant);
        product.Variants.Add(NewVariant(product, "SHIRT-HIDDEN", CatalogStatus.Hidden));
        product.Variants.Add(NewVariant(product, "SHIRT-NO-PRICE", CatalogStatus.Published));

        var result = ProductDetailsBuilder.Build(product, Now)!;

        Assert.Equal(product.Id, result.Id);
        Assert.Equal(category.Id, Assert.Single(result.Categories).Id);
        var shownVariant = Assert.Single(result.Variants);
        Assert.Equal(variant.Id, shownVariant.Id);
        Assert.Equal(3, shownVariant.QuantityOnHand);
        Assert.Equal(variant.ImageUrls, shownVariant.ImageUrls);
        Assert.Equal("kolor", Assert.Single(shownVariant.Attributes).Code);
        Assert.Collection(shownVariant.CurrentPrices,
            price => { Assert.Equal("EUR", price.Currency); Assert.Equal(20, price.Amount); },
            price => { Assert.Equal("PLN", price.Currency); Assert.Equal(85, price.Amount); });
    }

    [Fact]
    public void HiddenProductOrProductWithoutPricedPublishedVariantIsNotPublic()
    {
        var hidden = NewProduct();
        hidden.Status = CatalogStatus.Hidden;
        Assert.Null(ProductDetailsBuilder.Build(hidden, Now));

        var withoutPrice = NewProduct();
        withoutPrice.Variants.Add(NewVariant(withoutPrice, "NO-PRICE", CatalogStatus.Published));
        Assert.Null(ProductDetailsBuilder.Build(withoutPrice, Now));

        var withoutCategory = NewProduct();
        var pricedVariant = NewVariant(withoutCategory, "NO-CATEGORY", CatalogStatus.Published);
        pricedVariant.PriceChanges.Add(NewPrice(pricedVariant, "PLN", 50, Now.AddDays(-1), Now.AddDays(-1)));
        withoutCategory.Variants.Add(pricedVariant);
        Assert.Null(ProductDetailsBuilder.Build(withoutCategory, Now));
    }

    [Fact]
    public void MissingStockIsUnknownRatherThanZero()
    {
        var product = NewProduct();
        var category = new Category { DisplayName = "Koszulki", Slug = "koszulki" };
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id, Product = product,
            CategoryId = category.Id, Category = category
        });
        var variant = NewVariant(product, "NO-STOCK", CatalogStatus.Published);
        variant.PriceChanges.Add(NewPrice(variant, "PLN", 50, Now.AddDays(-1), Now.AddDays(-1)));
        product.Variants.Add(variant);
        var zeroStock = NewVariant(product, "ZERO-STOCK", CatalogStatus.Published);
        zeroStock.PriceChanges.Add(NewPrice(zeroStock, "PLN", 50, Now.AddDays(-1), Now.AddDays(-1)));
        zeroStock.Stock = new VariantStock { VariantId = zeroStock.Id, QuantityOnHand = 0 };
        product.Variants.Add(zeroStock);

        var shownVariants = ProductDetailsBuilder.Build(product, Now)!.Variants;
        Assert.Null(shownVariants.Single(shown => shown.Sku == "NO-STOCK").QuantityOnHand);
        Assert.Equal(0, shownVariants.Single(shown => shown.Sku == "ZERO-STOCK").QuantityOnHand);
    }

    private static Product NewProduct() => new()
    {
        Name = "Koszulka", Slug = "koszulka", Status = CatalogStatus.Published
    };

    private static ProductVariant NewVariant(Product product, string sku, CatalogStatus status) => new()
    {
        ProductId = product.Id, Product = product, SKU = sku, Status = status
    };

    private static VariantPriceChange NewPrice(
        ProductVariant variant, string currency, decimal amount, DateTime effectiveAt, DateTime createdAt) => new()
    {
        VariantId = variant.Id, Variant = variant, Currency = currency,
        BaseAmount = amount, SellingAmount = amount,
        EffectiveAt = effectiveAt, CreatedAt = createdAt, Reason = "Cena testowa"
    };
}
