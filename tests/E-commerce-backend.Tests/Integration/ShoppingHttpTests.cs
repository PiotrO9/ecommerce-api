using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Controllers;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class ShoppingHttpTests
{
    [PostgresFact]
    public async Task DeleteRoutesBindScopeAndVersionFromQueryAndProtectRetries()
    {
        await using var scenario = await ShoppingScenario.Create();
        await using var factory = new ShoppingApp(scenario);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.OK, (await Login(client, "a")).StatusCode);
        var cart = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Equal(HttpStatusCode.OK, (await PutCart(client, scenario.Cap, cart, 1)).StatusCode);
        cart = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        var query = $"?expectedScopeId={cart.ScopeId}&expectedVersion={cart.Version}";
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/web/cart/items/{scenario.Cap}{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/web/cart/items/{scenario.Cap}{query}")).StatusCode);
        cart = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Empty(cart.Items);
        Assert.Equal(HttpStatusCode.OK, (await PutCart(client, scenario.Cap, cart, 3)).StatusCode);
        cart = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/web/cart?expectedScopeId={cart.ScopeId}&expectedVersion={cart.Version}")).StatusCode);
        Assert.Empty((await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"))).Items);
        var favorites = await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/web/favorites/items/{scenario.Blue}", ShoppingPostgresTests.Stamp(favorites))).StatusCode);
        favorites = await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"));
        query = $"?expectedScopeId={favorites.ScopeId}&expectedVersion={favorites.Version}";
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/web/favorites/items/{scenario.Blue}{query}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/v1/web/favorites/items/{scenario.Blue}{query}")).StatusCode);
        Assert.Empty((await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"))).Items);
        var invalidPage = await client.GetAsync("/api/v1/web/favorites?pageSize=51");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode); NoStore(invalidPage);
    }

    [PostgresFact]
    public async Task RealHttpPipelineSeparatesGuestAccountLogoutAndAnotherAccount()
    {
        await using var scenario = await ShoppingScenario.Create();
        await using var factory = new ShoppingApp(scenario);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var empty = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Null(empty.ScopeId); Assert.False(empty.CanCheckout);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/web/favorites")).StatusCode);
        var init = await client.PostAsync("/api/v1/web/shopping/guest-session", null);
        Assert.Equal(HttpStatusCode.Created, init.StatusCode);
        var cookie = string.Join(";", init.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-ecomm-shopping=", cookie); Assert.Contains("secure", cookie);
        Assert.Contains("httponly", cookie); Assert.Contains("samesite=lax", cookie);
        var guest = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Equal(HttpStatusCode.OK, (await PutCart(client, scenario.Cap, guest, 1)).StatusCode);
        guest = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Single(guest.Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/v1/web/favorites/items/{scenario.Red}", ShoppingPostgresTests.Stamp(guest))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, "a")).StatusCode);
        var account = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Empty(account.Items); Assert.NotEqual(guest.ScopeId, account.ScopeId);
        Assert.Equal(HttpStatusCode.Conflict, (await PutCart(client, scenario.Cap, guest, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutCart(client, scenario.Red, account, 2)).StatusCode);
        var favorites = await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/web/favorites/items/{scenario.Blue}", ShoppingPostgresTests.Stamp(favorites))).StatusCode);
        favorites = await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"));
        Assert.Equal(scenario.Blue, Assert.Single(favorites.Items).VariantId);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/web/auth/logout", null)).StatusCode);
        var returned = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        Assert.Equal(guest.ScopeId, returned.ScopeId); Assert.Equal(scenario.Cap, Assert.Single(returned.Items).VariantId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/web/favorites")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(client, "b")).StatusCode);
        Assert.Empty((await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"))).Items);
        Assert.Empty((await Read<FavoritesResponse>(await client.GetAsync("/api/v1/web/favorites"))).Items);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/web/favorites/items/{scenario.Red}", ShoppingPostgresTests.Stamp(favorites))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/web/shopping/merge", null)).StatusCode);
    }

    [PostgresFact]
    public async Task HttpValidationCsrfAndAuthFailuresDoNotWriteOrFallbackToGuest()
    {
        await using var scenario = await ShoppingScenario.Create();
        await using var factory = new ShoppingApp(scenario);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var forbidden = await client.PostAsync("/api/v1/web/shopping/guest-session", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode); NoStore(forbidden);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        await Read<GuestShoppingSessionResponse>(await client.PostAsync("/api/v1/web/shopping/guest-session", null));
        var guest = await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"));
        var invalid = await client.PutAsJsonAsync($"/api/v1/web/cart/items/{scenario.Cap}", new
        { quantity = 1, expectedScopeId = guest.ScopeId, expectedVersion = guest.Version, userId = scenario.UserB });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); NoStore(invalid);
        var format = await client.PutAsync($"/api/v1/web/cart/items/{scenario.Cap}", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, format.StatusCode); NoStore(format);
        var quantityMissing = await client.PutAsJsonAsync($"/api/v1/web/cart/items/{scenario.Cap}", ShoppingPostgresTests.Stamp(guest));
        Assert.Equal(HttpStatusCode.BadRequest, quantityMissing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutCart(client, scenario.Cap, guest, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutCart(client, scenario.Cap, guest, 1)).StatusCode);
        using var invalidSession = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        invalidSession.DefaultRequestHeaders.Add("Cookie", "__Host-ecomm-auth=invalid");
        var invalidAuth = await invalidSession.GetAsync("/api/v1/web/cart");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidAuth.StatusCode); NoStore(invalidAuth);
        await Login(client, "a");
        factory.AuthUnavailable = true;
        var unavailable = await client.GetAsync("/api/v1/web/cart");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode); NoStore(unavailable);
        var noFallback = await PutCart(client, scenario.Cap, guest, 2);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noFallback.StatusCode);
        factory.AuthUnavailable = false;
        await client.PostAsync("/api/v1/web/auth/logout", null);
        Assert.Equal(1, Assert.Single((await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"))).Items).Quantity);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/web/shopping/guest-session")).StatusCode);
        Assert.Null((await Read<CartResponse>(await client.GetAsync("/api/v1/web/cart"))).ScopeId);
        // Configurable creation limit is active in the actual middleware.
        using var limitedFactory = new ShoppingApp(scenario, guestLimit: 1);
        using var limited = limitedFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        limited.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.Created, (await limited.PostAsync("/api/v1/web/shopping/guest-session", null)).StatusCode);
        var rejection = await limited.PostAsync("/api/v1/web/shopping/guest-session", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejection.StatusCode); NoStore(rejection);
        Assert.Equal("shopping_rate_limited", (await rejection.Content.ReadFromJsonAsync<ShoppingError>())!.Code);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string account) =>
        client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = $"{account}@example.com", password = "Test-only-password-123!" });
    private static Task<HttpResponseMessage> PutCart(HttpClient client, Guid id, CartResponse cart, int quantity) =>
        client.PutAsJsonAsync($"/api/v1/web/cart/items/{id}", new CartItemRequest { ExpectedScopeId = cart.ScopeId, ExpectedVersion = cart.Version, Quantity = quantity });
    private static void NoStore(HttpResponseMessage response) => Assert.True(response.Headers.CacheControl?.NoStore);
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        NoStore(response); return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private sealed class ShoppingApp(ShoppingScenario scenario, int guestLimit = 100) : WebApplicationFactory<Program>
    {
        public bool AuthUnavailable { get; set; }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal Program reads these before WebApplication.Build(). Host
            // configuration reaches CreateBuilder; app configuration is too late.
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SupabaseDatabase"] = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE"),
                ["Supabase:Url"] = "https://auth.example.test/", ["Supabase:PublishableKey"] = "test-public",
                ["Supabase:SecretKey"] = "test-secret", ["Mailtrap:ApiToken"] = "test-only",
                ["Mailtrap:FromEmail"] = "test@example.com", ["PasswordReset:Url"] = "https://localhost/reset-password",
                ["Shopping:GuestSessionsPerHour"] = guestLimit.ToString(), ["DemoSeed:Enabled"] = "false"
            }));
            return base.CreateHost(builder);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.AddHttpClient<SupabaseAuthClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new AuthHandler(this, scenario)));
        }
    }
    private sealed class AuthHandler(ShoppingApp app, ShoppingScenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (app.AuthUnavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                var token = body.GetProperty("email").GetString()!.StartsWith('a') ? "account-a" : "account-b";
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = token, refresh_token = "test-refresh" }) };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/logout")) return new(HttpStatusCode.NoContent);
            var id = request.Headers.Authorization?.Parameter == "account-a" ? scenario.UserA : scenario.UserB;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id, email = "test@example.com" }) };
        }
    }
}
