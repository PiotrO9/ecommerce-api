using E_commerce_backend.Controllers;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class ProductsEndpointTests
{
    [Fact]
    public async Task ProductLookupRequiresExactlyOneNonEmptySelector()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.Get(null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(Guid.NewGuid(), "produkt", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(null, "   ", CancellationToken.None));
    }

    [Fact]
    public async Task CategoryProductsRequireExactlyOneNonEmptySelector()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(null, null, null, null, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), "odziez", null, null, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(null, "   ", null, null, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CategoryProductsRejectInvalidPaginationBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);
        var categoryId = Guid.NewGuid();

        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 0, 20, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 1, 0, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 1, 101, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, int.MaxValue, 100, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task CatalogRejectsInvalidPaginationBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(0, null, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, 101, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(int.MaxValue, 100, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ProductListsRejectOverlongSearchBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);
        var overlong = new string('x', 101);

        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, overlong, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), null, null, null,
            overlong, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SearchRejectsInvalidBodyBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.Search(null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { CategoryId = Guid.NewGuid(), CategorySlug = "odziez" }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { CategorySlug = "  " }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { PageSize = 101 }, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { Q = new string('x', 101) }, CancellationToken.None));

        var missingValues = new ProductSearchRequest
        {
            Attributes = [new ProductAttributeFilterRequest { Code = "kolor" }]
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Search(missingValues, CancellationToken.None));

        var blankCode = new ProductSearchRequest
        {
            Attributes = [new ProductAttributeFilterRequest { Code = "  ", Values = ["czerwony"] }]
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Search(blankCode, CancellationToken.None));

        var tooManyValues = new ProductSearchRequest
        {
            Attributes = [new ProductAttributeFilterRequest
                { Code = "kolor", Values = Enumerable.Range(0, 31).Select(index => index.ToString()).ToList() }]
        };
        Assert.IsType<BadRequestObjectResult>(await controller.Search(tooManyValues, CancellationToken.None));
    }

    [Fact]
    public async Task ProductListsRejectFormerAttributeQueryBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Request.QueryString =
            new QueryString("?attributes%5B0%5D.code=kolor&attributes%5B0%5D.value=czerwony");

        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), null,
            null, null, null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ProductListsRejectUnknownSortBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, null,
            "unknown", null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), null,
            null, null, null, "unknown", null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { Sort = "unknown" }, CancellationToken.None));
    }

    [Fact]
    public async Task FilterOptionsRejectConflictingCategorySelectorsBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.GetFilterOptions(Guid.NewGuid(),
            "odziez", CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetFilterOptions(null,
            "  ", CancellationToken.None));
    }

    [Fact]
    public async Task ProductListsRejectInvalidPriceRangesBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var controller = new ProductsController(database);

        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, null, null,
            -1m, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, null, null,
            1.001m, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetCatalog(null, null, null, null,
            10000000000000000m, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), null,
            null, null, null, null, 120m, 100m, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Search(new ProductSearchRequest
            { MinPrice = 50m, MaxPrice = 49.99m }, CancellationToken.None));
    }
}
