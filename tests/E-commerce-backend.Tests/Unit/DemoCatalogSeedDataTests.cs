using E_commerce_backend.Entities;
using E_commerce_backend.Services.Development;

namespace E_commerce_backend.Tests.Unit;

public sealed class DemoCatalogSeedDataTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BaseCatalogIsCompleteBeforeDatabaseSave()
    {
        var data = DemoCatalogSeedData.Create(new DemoSeedOptions(new(3, 3), new(2, 2), new(1, 1)), Now);

        Assert.Equal(41, data.Entities.Count);
        Assert.Equal(new DemoSeedCreated(3, 2, 3, 2, 4, 2, 4, 3, 4, OfferEvents: 3, Promotions: 2, Collections: 4, CollectionProductLinks: 3, PromotionExclusions: 2), data.Created);
        Assert.All(data.Entities.OfType<VariantPriceChange>(), price =>
        {
            Assert.Null(price.OperationKey);
            Assert.Null(price.ChangedByUserId);
            Assert.Null(price.PreviousPriceChangeId);
        });
        Assert.All(data.Products, product => Assert.Equal(CatalogStatus.Draft, product.Status));
        Assert.All(data.Variants, variant => Assert.Equal(CatalogStatus.Draft, variant.Status));
        Assert.Equal(2, data.Entities.OfType<VariantPriceChange>()
            .Count(price => price.VariantId == data.Variants.Single(v => v.SKU == "DEMO-TSHIRT-RED-S").Id));
        AssertValidRelations(data);
    }

    [Fact]
    public void MaximumCatalogHasUniqueVariantsAndCompleteRelations()
    {
        var data = DemoCatalogSeedData.Create(new DemoSeedOptions(new(80, 80), new(500, 500), new(6, 6)), Now);

        Assert.Equal(80, data.Created.Categories);
        Assert.Equal(500, data.Created.Products);
        Assert.Equal(3000, data.Created.Variants);
        Assert.Equal(3000, data.Created.Stocks);
        Assert.Equal(3061, data.Created.PriceChanges);
        Assert.Equal(7, data.Created.Promotions);
        Assert.Equal(42, data.Created.PriceReductions);
        Assert.Equal(6, data.Created.Collections);
        Assert.Equal(10, data.Created.CollectionProductLinks);
        Assert.Equal(data.Entities.Count, data.Created.Sum());
        AssertValidRelations(data);

        var variantPairs = data.Entities.OfType<VariantAttributePair>().ToLookup(link => link.VariantId);
        foreach (var variants in data.Variants.GroupBy(variant => variant.ProductId))
        {
            Assert.Equal(6, variants.Count());
            Assert.Equal(6, variants.Select(variant =>
                string.Join(',', variantPairs[variant.Id].Select(link => link.AttributePairId).Order())).Distinct().Count());
        }
    }

    private static void AssertValidRelations(DemoCatalogSeedData data)
    {
        var categories = data.Entities.OfType<Category>().Select(category => category.Id).ToHashSet();
        var products = data.Products.Select(product => product.Id).ToHashSet();
        var variants = data.Variants.Select(variant => variant.Id).ToHashSet();
        var keys = data.Entities.OfType<AttributeKey>().Select(key => key.Id).ToHashSet();
        var pairs = data.Entities.OfType<AttributePair>().Select(pair => pair.Id).ToHashSet();

        Assert.All(data.Entities.OfType<Category>().Where(category => category.ParentId is not null),
            category => Assert.Contains(category.ParentId!.Value, categories));
        Assert.All(data.Entities.OfType<AttributePair>(), pair => Assert.Contains(pair.AttributeKeyId, keys));
        Assert.All(data.Variants, variant => Assert.Contains(variant.ProductId, products));
        Assert.All(data.Entities.OfType<ProductCategory>(), link =>
        {
            Assert.Contains(link.ProductId, products);
            Assert.Contains(link.CategoryId, categories);
        });
        Assert.All(data.Entities.OfType<VariantAttributePair>(), link =>
        {
            Assert.Contains(link.VariantId, variants);
            Assert.Contains(link.AttributePairId, pairs);
        });
        Assert.All(data.Entities.OfType<VariantStock>(), stock => Assert.Contains(stock.VariantId, variants));
        Assert.All(data.Entities.OfType<VariantPriceChange>(), price => Assert.Contains(price.VariantId, variants));
        Assert.All(data.Products, product => Assert.Contains(product.Id, data.Variants.Select(variant => variant.ProductId)));
        var collections = data.Entities.OfType<Collection>().Select(c => c.Id).ToHashSet();
        Assert.All(data.Entities.OfType<CollectionProduct>(), link =>
        {
            Assert.Contains(link.CollectionId, collections);
            Assert.Contains(link.ProductId, products);
        });
    }
}
