using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class CatalogModelTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time;Username=postgres")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void VariantOwnsOptionalStockAndManyPriceChanges()
    {
        using var database = CreateContext();
        var variant = database.Model.FindEntityType(typeof(ProductVariant))!;
        var stock = database.Model.FindEntityType(typeof(VariantStock))!;
        var price = database.Model.FindEntityType(typeof(VariantPriceChange))!;

        Assert.Equal("app", variant.GetSchema());
        Assert.Equal("product_variants", variant.GetTableName());
        Assert.Equal("text[]", variant.FindProperty(nameof(ProductVariant.ImageUrls))!.GetColumnType());
        Assert.Equal(typeof(Product), Assert.Single(variant.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.True(Assert.Single(stock.GetIndexes()).IsUnique);
        Assert.Equal(typeof(ProductVariant), Assert.Single(stock.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(typeof(ProductVariant), Assert.Single(price.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(18, price.FindProperty(nameof(VariantPriceChange.SellingAmount))!.GetPrecision());
        Assert.Equal(2, price.FindProperty(nameof(VariantPriceChange.SellingAmount))!.GetScale());
    }

    [Fact]
    public void CategoryHasSeparateUniqueIndexesForRootsAndSiblings()
    {
        using var database = CreateContext();
        var category = database.Model.FindEntityType(typeof(Category))!;
        var indexes = category.GetIndexes().Where(index => index.IsUnique).ToList();

        Assert.Equal(2, indexes.Count);
        Assert.Contains(indexes, index => index.GetFilter() == "parent_id IS NULL");
        Assert.Contains(indexes, index => index.GetFilter() == "parent_id IS NOT NULL");
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(category.GetForeignKeys()).DeleteBehavior);
    }

    [Fact]
    public void LinkTablesUseCompositeKeysAndRequiredForeignKeys()
    {
        using var database = CreateContext();
        foreach (var entityType in new[] { typeof(ProductCategory), typeof(VariantAttributePair) })
        {
            var entity = database.Model.FindEntityType(entityType)!;
            Assert.Equal("app", entity.GetSchema());
            Assert.Equal(2, entity.FindPrimaryKey()!.Properties.Count);
            Assert.Equal(2, entity.GetForeignKeys().Count());
            Assert.All(entity.GetForeignKeys(), foreignKey => Assert.True(foreignKey.IsRequired));
        }
    }

    [Theory]
    [InlineData("Kolor", "kolor")]
    [InlineData(" kolor ", "kolor")]
    [InlineData("KOLOR", "kolor")]
    public void AttributeCodeIsNormalizedBeforeUse(string input, string expected)
    {
        var key = new AttributeKey { Code = input, DisplayName = "Kolor" };
        Assert.Equal(expected, key.Code);
        Assert.Equal(expected, AttributeKey.NormalizeCode(input));
    }

    [Fact]
    public void EmptyAttributeCodeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AttributeKey.NormalizeCode(" \t "));
    }
}
