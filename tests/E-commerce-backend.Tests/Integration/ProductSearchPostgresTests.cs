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
