using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class CatalogManagementHttpTests
{
    private const string Admin = "/api/v1/web/admin";
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"HTTP {(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path, object? body, Guid? key = null)
    {
        var request = new HttpRequestMessage(method, Admin + path);
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }
    private static void NoStore(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore);

    [PostgresFact]
    public async Task HttpWorkflowPublishesAndHidesWithoutDeletingCartOrFavorites()
    {
        await using var fixture = await OfferApp.Create();
        using var admin = fixture.CreateClient(new() { BaseAddress = new("https://localhost") });
        admin.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "admin@example.test", password = "test" })).StatusCode);
        var category = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Post, "/categories", new { displayName = "Nowa kategoria" }), HttpStatusCode.Created);
        var key = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Post, "/attribute-keys", new { code = "Kolor", displayName = "Kolor" }), HttpStatusCode.Created);
        var pair = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Post, $"/attribute-keys/{key.Id}/values", new { value = "czerwony" }), HttpStatusCode.Created);
        var product = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Post, "/products", new
        {
            name = "Nowy produkt HTTP", categoryIds = new[] { category.Id },
            firstVariant = new { sku = "HTTP-RED-S", attributePairIds = new[] { pair.Id }, imageUrls = new[] { "https://example.test/image.png" } }
        }), HttpStatusCode.Created);
        var variantId = product.VariantId!.Value;
        var price = await Write(admin, HttpMethod.Post, $"/variants/{variantId}/price-changes", new { baseAmount = 25.50m, currency = "PLN", reason = "Cena", expectedPriceChangeId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, price.StatusCode);
        await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Put, $"/variants/{variantId}/stock", new { quantityOnHand = 5, expectedVersion = (long?)null, reason = "Przyjęcie" }), HttpStatusCode.Created);
        var variant = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Put, $"/variants/{variantId}/status", new { status = "Published", expectedVersion = product.VariantVersion }));
        var readiness = await Read<PublicationReadiness>(await admin.GetAsync($"{Admin}/products/{product.Id}/publication-readiness"));
        Assert.True(readiness.CanPublish); Assert.Equal(variant.ProductVersion, readiness.Version);
        var published = await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Put, $"/products/{product.Id}/status", new { status = "Published", expectedVersion = readiness.Version }));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/web/products?id={product.Id}")).StatusCode);
        var list = await Read<AdminPage<AdminProductRow>>(await admin.PostAsJsonAsync(Admin + "/products/search", new { q = "HTTP-RED", status = "Published", pageSize = 1 }));
        Assert.Equal(product.Id, Assert.Single(list.Items).Id); Assert.Equal(1, list.Pagination.TotalItems);
        Assert.Equal(variantId, Assert.Single((await Read<AdminPage<AdminVariantSummary>>(await admin.GetAsync($"{Admin}/products/{product.Id}/variants"))).Items).Variant.Id);
        await Read<AdminPage<AdminAttributeKey>>(await admin.GetAsync(Admin + "/attribute-keys"));
        await Read<AdminPage<AdminAttributePair>>(await admin.GetAsync($"{Admin}/attribute-keys/{key.Id}/values"));
        await Read<AdminCategoryNode[]>(await admin.GetAsync(Admin + "/categories"));

        using var customer = fixture.CreateClient(new() { BaseAddress = new("https://localhost") });
        customer.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        await customer.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "customer@example.test", password = "test" });
        var cart = await Read<CartResponse>(await customer.GetAsync("/api/v1/web/cart"));
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/v1/web/cart/items/{variantId}", new { quantity = 1, expectedScopeId = cart.ScopeId, expectedVersion = cart.Version })).StatusCode);
        var favorites = await Read<FavoritesResponse>(await customer.GetAsync("/api/v1/web/favorites"));
        Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/v1/web/favorites/items/{variantId}", new { expectedScopeId = favorites.ScopeId, expectedVersion = favorites.Version })).StatusCode);
        await Read<CatalogCommandResponse>(await Write(admin, HttpMethod.Put, $"/products/{product.Id}/status", new { status = "Hidden", expectedVersion = published.Version }));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/web/products?id={product.Id}")).StatusCode);
        var unavailable = Assert.Single((await Read<CartResponse>(await customer.GetAsync("/api/v1/web/cart"))).Items);
        Assert.Equal(variantId, unavailable.VariantId); Assert.Equal("unavailable", unavailable.Availability); Assert.Null(unavailable.Variant);
        Assert.Equal(variantId, Assert.Single((await Read<FavoritesResponse>(await customer.GetAsync("/api/v1/web/favorites"))).Items).VariantId);
    }

    [PostgresFact]
    public async Task AuthCsrfMalformedRequestsAndRevokedAdminNeverWrite()
    {
        await using var fixture = await OfferApp.Create();
        using var client = fixture.CreateClient(new() { BaseAddress = new("https://localhost") });
        var anonymous = await client.GetAsync(Admin + "/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); NoStore(anonymous);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "customer@example.test", password = "test" });
        var forbidden = await Write(client, HttpMethod.Post, "/categories", new { displayName = "Forbidden" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode); NoStore(forbidden);
        await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "admin@example.test", password = "test" });
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, HttpMethod.Post, "/categories", new { displayName = "CSRF" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var invalid = await Write(client, HttpMethod.Post, "/products", new { name = "X", actorUserId = fixture.AdminId });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); NoStore(invalid);
        invalid = await Write(client, HttpMethod.Put, $"/variants/{Guid.NewGuid()}/stock", new { quantityOnHand = 2, reason = "Missing version" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); NoStore(invalid);
        invalid = await client.PostAsJsonAsync(Admin + "/categories", new { displayName = "Missing operation key" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); NoStore(invalid);
        var badPage = await client.GetAsync(Admin + "/attribute-keys?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        fixture.AuthUnavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(Admin + "/categories")).StatusCode);
        fixture.AuthUnavailable = false;
        await using var db = CatalogManagementPostgresTests.Open();
        await db.Administrators.Where(a => a.UserId == fixture.AdminId).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Admin + "/categories")).StatusCode);
        Assert.Empty(await db.CatalogOperations.ToArrayAsync());
    }

    [PostgresFact]
    public async Task DeleteCategoryBindsQueryVersionAndReplaysAfterDeletion()
    {
        await using var fixture = await OfferApp.Create();
        using var client = fixture.CreateClient(new() { BaseAddress = new("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "admin@example.test", password = "test" });
        var category = await Read<CatalogCommandResponse>(await Write(client, HttpMethod.Post, "/categories", new { displayName = "Delete leaf" }), HttpStatusCode.Created);
        var key = Guid.NewGuid();
        var path = $"/categories/{category.Id}?expectedVersion={category.Version}";
        var deleted = await Write(client, HttpMethod.Delete, path, null, key);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode); Assert.Equal("", await deleted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, HttpMethod.Delete, path, null, key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Admin + $"/categories/{category.Id}")).StatusCode);
    }

    private sealed class OfferApp : WebApplicationFactory<Program>
    {
        public Guid AdminId { get; } = Guid.NewGuid();
        private Guid CustomerId { get; } = Guid.NewGuid();
        public bool AuthUnavailable { get; set; }
        public static async Task<OfferApp> Create()
        {
            await CatalogManagementPostgresTests.Seed();
            var app = new OfferApp();
            await using var db = CatalogManagementPostgresTests.Open();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.users(id) VALUES ({app.AdminId}),({app.CustomerId})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.administrators(user_id) VALUES ({app.AdminId})");
            return app;
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SupabaseDatabase"] = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE"),
                ["Supabase:Url"] = "https://auth.example.test/", ["Supabase:PublishableKey"] = "test-public",
                ["Supabase:SecretKey"] = "test-secret", ["Mailtrap:ApiToken"] = "test-only",
                ["Mailtrap:FromEmail"] = "test@example.com", ["PasswordReset:Url"] = "https://localhost/reset-password",
                ["DemoSeed:Enabled"] = "false"
            }));
            return base.CreateHost(builder);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new AuthHandler(this)));
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await using var db = CatalogManagementPostgresTests.Open();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id={AdminId} OR id={CustomerId}");
        }
        private sealed class AuthHandler(OfferApp app) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (app.AuthUnavailable) return new(HttpStatusCode.ServiceUnavailable);
                if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
                {
                    var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    var token = body.GetProperty("email").GetString()!.StartsWith("admin", StringComparison.Ordinal) ? "admin" : "customer";
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = token, refresh_token = "test-refresh" }) };
                }
                var id = request.Headers.Authorization?.Parameter == "admin" ? app.AdminId : app.CustomerId;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id, email = "test@example.com" }) };
            }
        }
    }
}
