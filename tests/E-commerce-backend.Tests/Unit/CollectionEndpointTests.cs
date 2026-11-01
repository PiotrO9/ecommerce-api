using System.Text.Json;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class CollectionEndpointTests
{
    private static AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options);
    [Theory]
    [InlineData(AdministratorAccessStatus.Unauthenticated, 401)]
    [InlineData(AdministratorAccessStatus.Forbidden, 403)]
    [InlineData(AdministratorAccessStatus.Unavailable, 503)]
    public async Task AllAdminCollectionRoutesRequireAccessBeforeDatabase(AdministratorAccessStatus status, int code)
    {
        await using var db = Open(); var id = Guid.NewGuid();
        var controller = new AdminCollectionsController(new Stub(status), new CollectionService(db, TimeProvider.System)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var results = new[] { await controller.List(), await controller.Get(id, default), await controller.Products(id),
            await controller.Create(null, null, null, default), await controller.Update(id, null, null, null, default), await controller.SetProducts(id, null, null, null, default) };
        Assert.All(results, r => Assert.Equal(code, Assert.IsAssignableFrom<ObjectResult>(r).StatusCode));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }
    [Fact]
    public async Task InvalidBodiesSelectorsAndPagingFailWithoutDatabase()
    {
        await using var db = Open(); var service = new CollectionService(db, TimeProvider.System); var id = Guid.NewGuid();
        Assert.Equal(400, (await service.GetAsync(false, id, "slug", default)).Status);
        Assert.Equal(400, (await service.ListAsync(false, 0, 20, null, default)).Status);
        Assert.Equal(400, (await service.MutateAsync("create", Guid.Empty, new() { DisplayName = "Name", Slug = "invalid slug" }, null, id, id, default)).Status);
        Assert.Equal(400, (await service.MutateAsync("members", id, null, new() { ExpectedVersion = 1, ProductIds = [id, id] }, id, id, default)).Status);
        var products = new ProductsController(db);
        Assert.IsType<BadRequestObjectResult>(await products.GetByCollection(id, "both", null, null, null, null, null, null, default));
        Assert.IsType<BadRequestObjectResult>(await products.Search(new() { CollectionSlug = " " }, default));
        Assert.IsType<BadRequestObjectResult>(await products.GetFilterOptions(null, null, default, id, "both"));
    }
    [Theory]
    [InlineData("actorUserId")]
    [InlineData("version")]
    [InlineData("createdAt")]
    public void MutationDtosRejectServerFields(string field)
    {
        var json = "{\"" + field + "\":null}"; var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionWriteRequest>(json, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CollectionProductsRequest>(json, options));
    }
    private sealed class Stub(AdministratorAccessStatus status) : IAdministratorAccess
    {
        public Task<AdministratorAccessResult> CheckAsync(HttpRequest request, HttpResponse response, CancellationToken ct) => Task.FromResult(new AdministratorAccessResult(status, Guid.NewGuid()));
    }
}
