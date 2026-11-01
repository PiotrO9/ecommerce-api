using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class PromotionEndpointTests
{
    [Theory]
    [InlineData(AdministratorAccessStatus.Unauthenticated, 401)]
    [InlineData(AdministratorAccessStatus.Forbidden, 403)]
    [InlineData(AdministratorAccessStatus.Unavailable, 503)]
    public async Task EveryRouteRequiresAdministratorBeforeDatabaseAccess(AdministratorAccessStatus status, int code)
    {
        await using var db = Database();
        var controller = Controller(db, status);
        var id = Guid.NewGuid();
        var results = new[] {
            await controller.List(), await controller.Get(id, default),
            await controller.Create(null, null, null, default), await controller.Update(id, null, null, null, default),
            await controller.Preview(id), await controller.Activate(id, null, null, null, default),
            await controller.End(id, null, null, null, default)
        };
        Assert.All(results, r => Assert.Equal(code, Assert.IsAssignableFrom<ObjectResult>(r).StatusCode));
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task InvalidWritesFailBeforeDatabaseAccess()
    {
        await using var db = Database();
        var controller = Controller(db, AdministratorAccessStatus.Allowed);
        Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(await controller.Create(null, null, null, default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.Create(null, "bad", "XMLHttpRequest", default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.Create(new(), Guid.NewGuid().ToString(), "XMLHttpRequest", default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.Activate(Guid.NewGuid(),
            new() { ExpectedVersion = 1, Variants = [new(Guid.NewGuid(), Guid.Empty)] }, Guid.NewGuid().ToString(), "XMLHttpRequest", default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.End(Guid.NewGuid(), new(), Guid.NewGuid().ToString(), "XMLHttpRequest", default)).StatusCode);
    }

    [Theory]
    [InlineData("actorUserId")]
    [InlineData("effectiveAt")]
    [InlineData("sellingAmount")]
    [InlineData("status")]
    public void ClientCannotSupplyServerOwnedFields(string field)
    {
        var json = "{\"" + field + "\":null}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PromotionDraftRequest>(json, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PromotionActivationRequest>(json, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PromotionVersionRequest>(json, options));
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options);
    private static AdminPromotionsController Controller(AppDbContext db, AdministratorAccessStatus status) =>
        new(new StubAccess(status), new PromotionService(db, TimeProvider.System))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    private sealed class StubAccess(AdministratorAccessStatus status) : IAdministratorAccess
    {
        public Task<AdministratorAccessResult> CheckAsync(HttpRequest request, HttpResponse response, CancellationToken ct) =>
            Task.FromResult(new AdministratorAccessResult(status, Guid.NewGuid()));
    }
}
