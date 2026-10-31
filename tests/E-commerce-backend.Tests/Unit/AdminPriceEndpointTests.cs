using System.Net;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class AdminPriceEndpointTests
{
    [Theory]
    [InlineData(AdministratorAccessStatus.Unauthenticated, 401)]
    [InlineData(AdministratorAccessStatus.Forbidden, 403)]
    [InlineData(AdministratorAccessStatus.Unavailable, 503)]
    public async Task AllEndpointsAuthorizeBeforeAccessingPrices(AdministratorAccessStatus status, int code)
    {
        await using var database = Database();
        var controller = Controller(database, new StubAccess(status));
        var id = Guid.NewGuid();
        Assert.Equal(code, Assert.IsAssignableFrom<ObjectResult>(await controller.GetPrice(id, default)).StatusCode);
        Assert.Equal(code, Assert.IsAssignableFrom<ObjectResult>(await controller.GetHistory(id, null, null, null, null, default)).StatusCode);
        Assert.Equal(code, Assert.IsAssignableFrom<ObjectResult>(await controller.ChangePrice(id, null, null, null, default)).StatusCode);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task NoSessionAndUnavailableAuthNeverQueryAdministratorTable()
    {
        await using var database = Database();
        var cookies = new AuthCookieService(new EphemeralDataProtectionProvider());
        using var client = new HttpClient(new Handler()) { BaseAddress = new Uri("https://example.invalid/") };
        var access = new AdministratorAccess(new AuthenticatedUserService(new SupabaseAuthClient(client), cookies), database);
        var context = new DefaultHttpContext();
        Assert.Equal(AdministratorAccessStatus.Unauthenticated,
            (await access.CheckAsync(context.Request, context.Response, default)).Status);
        var response = new DefaultHttpContext().Response;
        Assert.True(cookies.TryAppend(response, new AuthSessionPayload("test-access", "test-refresh", DateTimeOffset.UtcNow.AddHours(1))));
        context.Request.Headers.Cookie = response.Headers.SetCookie.ToString().Split(';')[0];
        Assert.Equal(AdministratorAccessStatus.Unavailable,
            (await access.CheckAsync(context.Request, context.Response, default)).Status);
    }

    [Fact]
    public async Task InvalidWriteAndHistoryInputsFailBeforePriceDatabaseAccess()
    {
        await using var database = Database();
        var controller = Controller(database, new StubAccess(AdministratorAccessStatus.Allowed));
        var id = Guid.NewGuid();
        var request = new ChangeVariantPriceRequest { BaseAmount = 5m, Reason = "Test" };
        Assert.Equal(403, Assert.IsAssignableFrom<ObjectResult>(await controller.ChangePrice(id, request,
            Guid.NewGuid().ToString(), null, default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.ChangePrice(id, request,
            "not-a-uuid", "XMLHttpRequest", default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.ChangePrice(id, new(),
            Guid.NewGuid().ToString(), "XMLHttpRequest", default)).StatusCode);
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.ChangePrice(id,
            new() { BaseAmount = 1.001m, Reason = "Test" }, Guid.NewGuid().ToString(), "XMLHttpRequest", default)).StatusCode);
        foreach (var (page, size) in new[] { (0, 20), (1, 101), (int.MaxValue, 100) })
            Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.GetHistory(id, page, size, null, null, default)).StatusCode);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(await controller.GetHistory(id, null, null, now, now, default)).StatusCode);
    }

    [Theory]
    [InlineData("actorUserId")]
    [InlineData("sellingAmount")]
    [InlineData("effectiveAt")]
    public void WriteBodyRejectsServerOwnedFields(string field)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ChangeVariantPriceRequest>(
            "{\"baseAmount\":10,\"" + field + "\":null}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused").Options);

    private static AdminVariantPricesController Controller(AppDbContext database, IAdministratorAccess access) =>
        new(access, database, new VariantPriceService(database, TimeProvider.System), TimeProvider.System)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

    private sealed class StubAccess(AdministratorAccessStatus status) : IAdministratorAccess
    {
        public Task<AdministratorAccessResult> CheckAsync(HttpRequest request, HttpResponse response,
            CancellationToken cancellationToken) => Task.FromResult(new AdministratorAccessResult(status, Guid.NewGuid()));
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
