using System.Net;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class AdminPricePostgresTests
{
    [PostgresFact]
    public async Task VerifiedIdentityNeedsDatabaseMembershipAndRevocationAppliesImmediately()
    {
        await using var database = Open();
        await Seed(database);
        var id = await database.ProductVariants.Select(v => v.Id).FirstAsync();
        var userId = Guid.NewGuid();
        using var fixture = new Fixture(database, userId);
        // Supabase stub even claims admin in user_metadata; membership in app is still required.
        Assert.Equal(403, Status(await fixture.Controller.GetPrice(id, default)));
        await Grant(database, userId);
        Assert.Equal(200, Status(await fixture.Controller.GetPrice(id, default)));
        await Seed(database);
        Assert.True(await database.Administrators.AnyAsync(a => a.UserId == userId));
        var newId = await database.ProductVariants.Select(v => v.Id).FirstAsync();
        Assert.Equal(200, Status(await fixture.Controller.GetPrice(newId, default)));
        await database.Administrators.Where(a => a.UserId == userId).ExecuteDeleteAsync();
        Assert.Equal(403, Status(await fixture.Controller.GetPrice(newId, default)));
        Assert.Equal(403, Status(await fixture.Controller.ChangePrice(newId,
            new() { BaseAmount = 10, Reason = "Test" }, Guid.NewGuid().ToString(), "XMLHttpRequest", default)));
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id = {userId}");
    }

    [PostgresFact]
    public async Task AdminCanChangeReadAndPagePricesWithAuditAndReplay()
    {
        await using var database = Open();
        await Seed(database);
        var userId = Guid.NewGuid();
        await Grant(database, userId);
        using var fixture = new Fixture(database, userId);
        var controller = fixture.Controller;
        var cap = await database.ProductVariants.SingleAsync(v => v.SKU == "DEMO-CAP-ONE");
        var initial = Value<CurrentVariantPriceResponse>(await controller.GetPrice(cap.Id, default));
        Assert.Equal(50m, initial.Price!.SellingAmount);
        var key = Guid.NewGuid().ToString();
        var request = new ChangeVariantPriceRequest { BaseAmount = 79.99m, Reason = "Nowy cennik", ExpectedPriceChangeId = initial.Price.Id };
        var createdResult = await controller.ChangePrice(cap.Id, request, key, "XMLHttpRequest", default);
        Assert.Equal(201, Status(createdResult));
        var created = Value<ChangeVariantPriceResponse>(createdResult);
        Assert.False(created.Replayed);
        Assert.Equal(userId, created.Price.ChangedByUserId);
        var replayResult = await controller.ChangePrice(cap.Id, request, key, "XMLHttpRequest", default);
        Assert.Equal(200, Status(replayResult));
        var replay = Value<ChangeVariantPriceResponse>(replayResult);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Price, replay.Price);
        var conflict = await controller.ChangePrice(cap.Id, request, Guid.NewGuid().ToString(), "XMLHttpRequest", default);
        Assert.Equal(409, Status(conflict));
        Assert.Contains("price_version_conflict", JsonSerializer.Serialize(((ObjectResult)conflict).Value));

        var current = Value<CurrentVariantPriceResponse>(await controller.GetPrice(cap.Id, default));
        Assert.Equal(created.Price, current.Price);
        var page1 = Value<PriceHistoryResponse>(await controller.GetHistory(cap.Id, 1, 1, null, null, default));
        var page2 = Value<PriceHistoryResponse>(await controller.GetHistory(cap.Id, 2, 1, null, null, default));
        Assert.Equal(2, page1.TotalItems);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(created.Price.Id, Assert.Single(page1.Items).Id);
        Assert.Equal(initial.Price.Id, Assert.Single(page2.Items).Id);
        var from = new DateTimeOffset(created.Price.EffectiveAt).ToOffset(TimeSpan.FromHours(2));
        var filtered = Value<PriceHistoryResponse>(await controller.GetHistory(cap.Id, null, null, from, null, default));
        Assert.Equal(created.Price.Id, Assert.Single(filtered.Items).Id);
        var before = Value<PriceHistoryResponse>(await controller.GetHistory(cap.Id, null, null, null, from, default));
        Assert.Equal(initial.Price.Id, Assert.Single(before.Items).Id);

        var details = Value<ProductDetailsResponse>(await new ProductsController(database).Get(cap.ProductId, null, default));
        Assert.Equal(79.99m, Assert.Single(Assert.Single(details.Variants).CurrentPrices).Amount);
        var missing = Guid.NewGuid();
        Assert.Equal(404, Status(await controller.GetPrice(missing, default)));
        Assert.Equal(404, Status(await controller.GetHistory(missing, null, null, null, null, default)));
        Assert.Equal(404, Status(await controller.ChangePrice(missing,
            new() { BaseAmount = 10, Reason = "Test" }, Guid.NewGuid().ToString(), "XMLHttpRequest", default)));

        var product = new Product { Name = "Szkic", Slug = "draft-" + Guid.NewGuid().ToString("N") };
        var variant = new ProductVariant { Product = product, SKU = "DRAFT-" + Guid.NewGuid().ToString("N") };
        database.ProductVariants.Add(variant);
        await database.SaveChangesAsync();
        Assert.Null(Value<CurrentVariantPriceResponse>(await controller.GetPrice(variant.Id, default)).Price);
        Assert.Empty(Value<PriceHistoryResponse>(await controller.GetHistory(variant.Id, null, null, null, null, default)).Items);
        Assert.Equal(201, Status(await controller.ChangePrice(variant.Id, new() { BaseAmount = 15, Reason = "Pierwsza cena" },
            Guid.NewGuid().ToString(), "XMLHttpRequest", default)));
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id = {userId}");
        Assert.False(await database.Administrators.AnyAsync(a => a.UserId == userId));
    }

    [PostgresFact]
    public async Task DiscountAndScheduledPriceHaveDistinctConflictsAndHistoryShowsFuture()
    {
        await using var database = Open();
        await Seed(database);
        var userId = Guid.NewGuid();
        await Grant(database, userId);
        using var fixture = new Fixture(database, userId);
        var id = await database.ProductVariants.Where(v => v.SKU == "DEMO-TSHIRT-RED-S").Select(v => v.Id).SingleAsync();
        var current = Value<CurrentVariantPriceResponse>(await fixture.Controller.GetPrice(id, default)).Price!;
        var conflict = await fixture.Controller.ChangePrice(id, new() { BaseAmount = 100, Reason = "Test", ExpectedPriceChangeId = current.Id },
            Guid.NewGuid().ToString(), "XMLHttpRequest", default);
        Assert.Equal(409, Status(conflict));
        Assert.Contains("discounted_price_conflict", JsonSerializer.Serialize(((ObjectResult)conflict).Value));
        var capId = await database.ProductVariants.Where(v => v.SKU == "DEMO-CAP-ONE").Select(v => v.Id).SingleAsync();
        var capPrice = Value<CurrentVariantPriceResponse>(await fixture.Controller.GetPrice(capId, default)).Price!;
        database.VariantPriceChanges.Add(new VariantPriceChange { VariantId = capId, BaseAmount = 200,
            SellingAmount = 200, Reason = "Przyszła", EffectiveAt = DateTime.UtcNow.AddDays(1) });
        await database.SaveChangesAsync();
        Assert.Equal(capPrice.Id, Value<CurrentVariantPriceResponse>(await fixture.Controller.GetPrice(capId, default)).Price!.Id);
        var futureConflict = await fixture.Controller.ChangePrice(capId,
            new() { BaseAmount = 100, Reason = "Test", ExpectedPriceChangeId = capPrice.Id },
            Guid.NewGuid().ToString(), "XMLHttpRequest", default);
        Assert.Equal(409, Status(futureConflict));
        Assert.Contains("future_price_conflict", JsonSerializer.Serialize(((ObjectResult)futureConflict).Value));
        Assert.Equal(2, Value<PriceHistoryResponse>(await fixture.Controller.GetHistory(capId, null, null, null, null, default)).TotalItems);
        await database.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id = {userId}");
    }

    private static AppDbContext Open()
    {
        var connectionString = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(connectionString);
        if (address.Host is not ("127.0.0.1" or "localhost") || address.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Test wymaga izolowanej lokalnej bazy ecomm_test_*.");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
    private static async Task Seed(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        await new DemoCatalogSeeder(database).SeedAsync(new(new(3, 3), new(2, 2), new(1, 1)), default);
        database.ChangeTracker.Clear();
    }
    private static async Task Grant(AppDbContext database, Guid id)
    {
        await database.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.users (id) VALUES ({id})");
        database.Administrators.Add(new() { UserId = id });
        await database.SaveChangesAsync();
    }
    private static int? Status(IActionResult result) => Assert.IsAssignableFrom<ObjectResult>(result).StatusCode;
    private static T Value<T>(IActionResult result) => Assert.IsType<T>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient client;
        public AdminVariantPricesController Controller { get; }
        public Fixture(AppDbContext database, Guid id)
        {
            var cookies = new AuthCookieService(new EphemeralDataProtectionProvider());
            client = new HttpClient(new UserHandler(id)) { BaseAddress = new Uri("https://example.invalid/") };
            var access = new AdministratorAccess(new AuthenticatedUserService(new SupabaseAuthClient(client), cookies), database);
            Controller = new(access, database, new VariantPriceService(database, TimeProvider.System), TimeProvider.System)
                { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
            var response = new DefaultHttpContext().Response;
            Assert.True(cookies.TryAppend(response, new AuthSessionPayload("access", "refresh", DateTimeOffset.UtcNow.AddHours(1))));
            Controller.Request.Headers.Cookie = response.Headers.SetCookie.ToString().Split(';')[0];
        }
        public void Dispose() => client.Dispose();
    }
    private sealed class UserHandler(Guid userId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                $"{{\"id\":\"{userId}\",\"email\":\"test@example.invalid\",\"user_metadata\":{{\"role\":\"admin\"}}}}", Encoding.UTF8, "application/json") });
    }
}
