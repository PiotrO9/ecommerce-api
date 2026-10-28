using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Development;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

public sealed class ProductSearchPostgresTests
{
    [PostgresFact]
    public async Task SearchRanksMatchingVariantsBeforePaginationAndRespectsExplicitSort()
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(connectionString);
        if (address.Host is not ("127.0.0.1" or "localhost") ||
            address.Database is null || !address.Database.StartsWith("ecomm_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Test wymaga lokalnej, izolowanej bazy ecomm_test_*; seeder czyści katalog.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var database = new AppDbContext(options);
        await new DemoCatalogSeeder(database).SeedAsync(
            new DemoSeedOptions(new(3, 3), new(2, 2), new(1, 1)), CancellationToken.None);

        var categoryId = await database.Categories.Where(category => category.Slug == "demo-akcesoria")
            .Select(category => category.Id).SingleAsync();
        var redPairId = await database.AttributePairs.Where(pair =>
                pair.AttributeKey.Code == "demo-kolor" && pair.Value == "czerwony")
            .Select(pair => pair.Id).SingleAsync();
        var bluePairId = await database.AttributePairs.Where(pair =>
                pair.AttributeKey.Code == "demo-kolor" && pair.Value == "niebieski")
            .Select(pair => pair.Id).SingleAsync();

        AddSearchProduct(database, categoryId, "rank-exact-name", "needle", ("RANK-NAME", 50m, null));
        AddSearchProduct(database, categoryId, "rank-exact-sku", "Zulu", ("needle", 50m, null));
        AddSearchProduct(database, categoryId, "rank-name-prefix", "Needle starter", ("RANK-PREFIX", 50m, null));
        AddSearchProduct(database, categoryId, "rank-sku-prefix", "B SKU prefix", ("needle-extra", 50m, null));
        AddSearchProduct(database, categoryId, "rank-name-contains", "The needle case", ("RANK-CONTAINS", 50m, null));
        AddSearchProduct(database, categoryId, "rank-sku-contains", "C SKU contains", ("X-needle-X", 50m, null));
        AddSearchProduct(database, categoryId, "rank-filtered", "A filtered",
            ("Needle-filtered", 1000m, redPairId), ("X-needle-ok", 50m, bluePairId));
        AddSearchProduct(database, categoryId, "rank-literal", @"x%_\y", ("RANK-LITERAL", 50m, null));
        AddSearchProduct(database, categoryId, "rank-wildcard-decoy", @"xAB\y", ("RANK-DECOY", 50m, null));
        await database.SaveChangesAsync();

        var controller = new ProductsController(database);
        var firstPage = await SearchAsync(controller, new ProductSearchRequest
            { Q = "  NEEDLE  ", Page = 1, PageSize = 2 });
        Assert.Equal(7, firstPage.Pagination!.TotalItems);
        Assert.Equal(["rank-exact-name", "rank-exact-sku"], firstPage.Items.Select(item => item.Slug));

        var secondPage = await SearchAsync(controller, new ProductSearchRequest
            { Q = "needle", Page = 2, PageSize = 2 });
        Assert.Equal(["rank-name-prefix", "rank-filtered"], secondPage.Items.Select(item => item.Slug));

        var filtered = await SearchAsync(controller, new ProductSearchRequest
            { Q = "needle", MaxPrice = 100m, PageSize = 20 });
        Assert.Equal([
            "rank-exact-name", "rank-exact-sku", "rank-name-prefix", "rank-sku-prefix",
            "rank-name-contains", "rank-filtered", "rank-sku-contains"
        ], filtered.Items.Select(item => item.Slug));

        var explicitSort = await SearchAsync(controller, new ProductSearchRequest
            { Q = "needle", Sort = "name-desc", PageSize = 20 });
        Assert.Equal("rank-exact-sku", explicitSort.Items[0].Slug);
        Assert.Equal("rank-filtered", explicitSort.Items[^1].Slug);

        var literal = await SearchAsync(controller, new ProductSearchRequest { Q = @"x%_\y" });
        Assert.Equal("rank-literal", Assert.Single(literal.Items).Slug);

        var get = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCatalog(1, 2, "needle", null, null, null, CancellationToken.None)).Value);
        Assert.Equal(firstPage.Items.Select(item => item.Slug), get.Items.Select(item => item.Slug));

        var byCategory = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetByCategory(categoryId, null, 1, 2, "needle", null, null, null,
                CancellationToken.None)).Value);
        Assert.Equal(firstPage.Items.Select(item => item.Slug), byCategory.Items.Select(item => item.Slug));
    }

    [PostgresFact]
    public async Task SearchKeepsVariantPriceAndFacetRulesOnPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(connectionString);
        if (address.Host is not ("127.0.0.1" or "localhost") ||
            address.Database is null || !address.Database.StartsWith("ecomm_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Test wymaga lokalnej, izolowanej bazy ecomm_test_*; seeder czyści katalog.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var database = new AppDbContext(options);
        await new DemoCatalogSeeder(database).SeedAsync(
            new DemoSeedOptions(new(3, 3), new(2, 2), new(1, 1)), CancellationToken.None);

        var redVariant = await database.ProductVariants.SingleAsync(variant => variant.SKU == "DEMO-TSHIRT-RED-S");
        database.VariantPriceChanges.Add(new VariantPriceChange
        {
            VariantId = redVariant.Id,
            BaseAmount = 120m,
            SellingAmount = 10m,
            EffectiveAt = DateTime.UtcNow.AddDays(1),
            Reason = "Test: przyszła cena"
        });

        var categoryId = await database.Categories.Where(category => category.Slug == "demo-akcesoria")
            .Select(category => category.Id).SingleAsync();
        var foreignProduct = new Product
        {
            Name = "Produkt walutowy",
            Slug = "test-usd-only",
            Status = CatalogStatus.Published,
            PublishedAt = DateTime.UtcNow
        };
        var foreignVariant = new ProductVariant
        {
            ProductId = foreignProduct.Id,
            SKU = "TEST-USD-ONLY",
            Status = CatalogStatus.Published,
            PublishedAt = DateTime.UtcNow
        };
        database.AddRange(foreignProduct, foreignVariant,
            new ProductCategory { ProductId = foreignProduct.Id, CategoryId = categoryId },
            new VariantPriceChange
            {
                VariantId = foreignVariant.Id,
                BaseAmount = 25m,
                SellingAmount = 25m,
                Currency = "USD",
                EffectiveAt = DateTime.UtcNow.AddDays(-1),
                Reason = "Test: bez ceny PLN"
            });
        await database.SaveChangesAsync();

        var controller = new ProductsController(database);
        var mismatch = await SearchAsync(controller, new ProductSearchRequest
        {
            Q = "Koszulka demo",
            Attributes =
            [
                new ProductAttributeFilterRequest { Code = "demo-kolor", Values = ["czerwony"] },
                new ProductAttributeFilterRequest { Code = "demo-rozmiar", Values = ["M"] }
            ]
        });
        Assert.Empty(mismatch.Items);
        Assert.Equal((0, true), Facet(mismatch, "demo-kolor", "czerwony"));
        Assert.Equal((1, false), Facet(mismatch, "demo-kolor", "niebieski"));
        Assert.Equal((1, false), Facet(mismatch, "demo-rozmiar", "S"));
        Assert.Equal((0, true), Facet(mismatch, "demo-rozmiar", "M"));

        var eitherColor = await SearchAsync(controller, new ProductSearchRequest
        {
            Q = "Koszulka demo",
            Attributes = [new ProductAttributeFilterRequest
                { Code = "demo-kolor", Values = ["czerwony", "niebieski"] }]
        });
        Assert.Single(eitherColor.Items);
        Assert.Equal(1, Facet(eitherColor, "demo-kolor", "czerwony").Count);
        Assert.Equal(1, Facet(eitherColor, "demo-kolor", "niebieski").Count);

        var red = await SearchAsync(controller, new ProductSearchRequest
        {
            Q = "Koszulka demo",
            MinPrice = 90m,
            MaxPrice = 90m,
            Attributes =
            [
                new ProductAttributeFilterRequest { Code = "demo-kolor", Values = ["czerwony"] },
                new ProductAttributeFilterRequest { Code = "demo-rozmiar", Values = ["S"] }
            ]
        });
        Assert.Equal("demo-koszulka", Assert.Single(red.Items).Slug);
        Assert.Equal(90m, Assert.Single(Assert.Single(red.Items).PricesFrom).Amount);

        var blue = await SearchAsync(controller, new ProductSearchRequest
        {
            Q = "Koszulka demo", MinPrice = 100m, MaxPrice = 110m
        });
        Assert.Equal(110m, Assert.Single(Assert.Single(blue.Items).PricesFrom).Amount);

        var crossed = await SearchAsync(controller, new ProductSearchRequest
        {
            Q = "DEMO-TSHIRT-RED-S", MinPrice = 100m, MaxPrice = 110m
        });
        Assert.Empty(crossed.Items);

        var ascending = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCatalog(1, 20, null, "price-asc", null, null, CancellationToken.None)).Value);
        Assert.Equal(["demo-czapka", "demo-koszulka", "test-usd-only"], ascending.Items.Select(item => item.Slug));
        Assert.Equal(3, ascending.Pagination!.TotalItems);

        var descending = Assert.IsType<ProductListResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCatalog(1, 20, null, "price-desc", null, null, CancellationToken.None)).Value);
        Assert.Equal(["demo-koszulka", "demo-czapka", "test-usd-only"], descending.Items.Select(item => item.Slug));

        var plnOnly = await SearchAsync(controller, new ProductSearchRequest
        {
            MaxPrice = 500m
        });
        Assert.Equal(2, plnOnly.Pagination!.TotalItems);
        Assert.DoesNotContain(plnOnly.Items, item => item.Slug == "test-usd-only");

        var category = await SearchAsync(controller, new ProductSearchRequest
        {
            CategorySlug = "demo-akcesoria"
        });
        Assert.Equal(["demo-czapka", "test-usd-only"],
            category.Items.Select(item => item.Slug));
    }

    private static async Task<ProductSearchResponse> SearchAsync(
        ProductsController controller, ProductSearchRequest request) =>
        Assert.IsType<ProductSearchResponse>(Assert.IsType<OkObjectResult>(
            await controller.Search(request, CancellationToken.None)).Value);

    private static void AddSearchProduct(AppDbContext database, Guid categoryId, string slug,
        string name, params (string Sku, decimal Price, Guid? AttributePairId)[] variants)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Name = name,
            Slug = slug,
            Status = CatalogStatus.Published,
            PublishedAt = now
        };
        database.Products.Add(product);
        database.ProductCategories.Add(new ProductCategory { ProductId = product.Id, CategoryId = categoryId });
        foreach (var (sku, price, attributePairId) in variants)
        {
            var variant = new ProductVariant
            {
                ProductId = product.Id,
                SKU = sku,
                Status = CatalogStatus.Published,
                PublishedAt = now
            };
            database.ProductVariants.Add(variant);
            database.VariantPriceChanges.Add(new VariantPriceChange
            {
                VariantId = variant.Id,
                BaseAmount = price,
                SellingAmount = price,
                EffectiveAt = now.AddDays(-1),
                Reason = "Test rankingu"
            });
            if (attributePairId is Guid pairId)
            {
                database.VariantAttributePairs.Add(new VariantAttributePair
                    { VariantId = variant.Id, AttributePairId = pairId });
            }
        }
    }

    private static (int Count, bool Selected) Facet(ProductSearchResponse response, string code, string value)
    {
        var option = response.AvailableFilters.Single(filter => filter.Code == code)
            .Values.Single(item => item.Value == value);
        return (option.Count, option.Selected);
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")))
        {
            Skip = "Ustaw ECOMMERCE_TEST_DATABASE na izolowaną bazę PostgreSQL z migracjami.";
        }
    }
}
