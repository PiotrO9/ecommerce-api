using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_commerce_backend.Contracts.Inventory;
using E_commerce_backend.Contracts.Shopping;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class InventoryHttpTests
{
    [PostgresFact]
    public async Task OrdersRequireAccountCsrfAndIdempotencyAndArePrivateBetweenAccounts()
    {
        await using var s = await InventoryScenario.Create();
        await using var factory = new ShoppingHttpTests.ShoppingApp(s.Shopping);
        using var a = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var b = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        var anonymous = await a.GetAsync("/api/v1/web/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); NoStore(anonymous);
        var request = await s.Request(s.Shopping.AccountA, (s.Shopping.Cap, 2));
        await Login(a, "a@example.test"); await Login(b, "b@example.test");
        var noCsrf = await a.PostAsJsonAsync("/api/v1/web/orders", request);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode); NoStore(noCsrf);
        a.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var missingKey = await a.PostAsJsonAsync("/api/v1/web/orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        var key = Guid.NewGuid();
        a.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
        var unknown = await a.PostAsJsonAsync("/api/v1/web/orders", new
        {
            request.ExpectedScopeId, request.ExpectedVersion, request.Items, userId = s.Shopping.UserB
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode); NoStore(unknown);
        var malformed = await a.PostAsync("/api/v1/web/orders", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, malformed.StatusCode); NoStore(malformed);
        var created = await a.PostAsJsonAsync("/api/v1/web/orders", request);
        var order = await Read<OrderResponse>(created, HttpStatusCode.Created);
        Assert.Equal("PendingPayment", order.Status);
        Assert.Equal(15, (order.ExpiresAt - order.CreatedAt).TotalMinutes);
        Assert.True((await Read<OrderResponse>(await a.PostAsJsonAsync("/api/v1/web/orders", request))).Replayed);
        Assert.Empty((await Read<CartResponse>(await a.GetAsync("/api/v1/web/cart"))).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/web/orders/{order.Id}")).StatusCode);
        b.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/v1/web/orders/{order.Id}/cancel", null)).StatusCode);
        Assert.Equal(0, (await Read<OrderPage>(await b.GetAsync("/api/v1/web/orders"))).TotalItems);
        Assert.Equal(1, (await Read<OrderPage>(await a.GetAsync("/api/v1/web/orders"))).TotalItems);
        var cancelled = await Read<OrderResponse>(await a.PostAsync($"/api/v1/web/orders/{order.Id}/cancel", null));
        Assert.Equal("Cancelled", cancelled.Status); Assert.Equal("Released", cancelled.ReservationStatus);
        Assert.Equal("Cancelled", (await Read<OrderResponse>(await a.PostAsync($"/api/v1/web/orders/{order.Id}/cancel", null))).Status);
        // There is deliberately no client-callable settlement endpoint.
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync($"/api/v1/web/orders/{order.Id}/pay", null)).StatusCode);
        factory.AuthUnavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await a.GetAsync("/api/v1/web/orders")).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidPaginationAndBodyDoNotCreateReservations()
    {
        await using var s = await InventoryScenario.Create();
        await using var factory = new ShoppingHttpTests.ShoppingApp(s.Shopping);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(client, "a@example.test");
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100" })
        {
            var result = await client.GetAsync("/api/v1/web/orders?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); NoStore(result);
        }
        foreach (var body in new[] { "{}", "null", "{\"items\":[null]}" })
        {
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var result = await client.PostAsync("/api/v1/web/orders", content);
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); NoStore(result);
        }
        await using var db = ShoppingScenario.Open();
        Assert.Empty(db.Orders); Assert.Empty(db.StockReservations);
    }

    [PostgresFact]
    public async Task OrderWritesUseRateLimiterAndNeverCacheItsError()
    {
        await using var s = await InventoryScenario.Create();
        await using var factory = new ShoppingHttpTests.ShoppingApp(s.Shopping, writeLimit: 1);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(client, "a@example.test");
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/web/orders", new { })).StatusCode);
        var limited = await client.PostAsJsonAsync("/api/v1/web/orders", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); NoStore(limited);
        Assert.Equal("shopping_rate_limited", (await limited.Content.ReadFromJsonAsync<ShoppingError>())!.Code);
    }

    private static async Task Login(HttpClient client, string email) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email, password = "test" })).StatusCode);
    private static void NoStore(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore);
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"HTTP {(int)response.StatusCode}: {body}");
        NoStore(response);
        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
