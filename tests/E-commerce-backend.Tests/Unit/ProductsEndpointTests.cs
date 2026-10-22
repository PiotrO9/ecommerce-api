using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
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

        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(null, null, null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(Guid.NewGuid(), "odziez", null, null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(null, "   ", null, null, CancellationToken.None));
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

        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 0, 20, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 1, 0, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, 1, 101, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.GetByCategory(categoryId, null, int.MaxValue, 100, CancellationToken.None));
    }
}
