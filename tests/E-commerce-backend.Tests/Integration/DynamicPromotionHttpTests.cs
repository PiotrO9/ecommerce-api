using System.Net;
using System.Net.Http.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Services.Development;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class DynamicPromotionHttpTests
{
    private const string Api = "/api/v1/web/admin/promotions";
    private static Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path, object body, Guid? key = null)
    {
        var r = new HttpRequestMessage(method, Api + path) { Content = JsonContent.Create(body) };
        r.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return client.SendAsync(r);
    }

    [PostgresFact]
    public async Task DynamicContractUsesScopeTokenAndExposesProtectedDiagnosticsAndExclusions()
    {
        await using var app = await CatalogManagementHttpTests.OfferApp.Create();
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Api + "/conflicts")).StatusCode);
        await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "customer@example.test", password = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Api + "/conflicts")).StatusCode);
        await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "admin@example.test", password = "test" });
        var draft = new { scope = "catalog", name = "Cały katalog", discountType = "percentage", discountValue = 20 };
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(client, HttpMethod.Post, "", draft)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Api, draft)).StatusCode);
        var created = await Write(client, HttpMethod.Post, "", draft);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.True(created.Headers.CacheControl?.NoStore);
        var p = (await created.Content.ReadFromJsonAsync<PromotionCommandResponse>())!.Promotion;
        Assert.Equal("catalog", p.Scope); Assert.Equal(0, p.ActiveVariants);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, HttpMethod.Post, "", new { scope = "category", categoryId = Guid.NewGuid(), name = "X", discountType = "percentage", discountValue = 20, unknown = true })).StatusCode);
        var previewResponse = await Write(client, HttpMethod.Post, $"/{p.Id}/preview?pageSize=1", new { });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<PromotionPreviewResponse>())!;
        Assert.Single(preview.Items); Assert.NotNull(preview.ScopeToken);
        var conflict = await Write(client, HttpMethod.Post, $"/{p.Id}/activate", new { expectedVersion = p.Version, expectedScopeToken = preview.ScopeToken });
        // This deliberately mixed seed contains existing active product/collection campaigns.
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflicts = await client.GetFromJsonAsync<PromotionConflictPage>(Api + $"/conflicts?promotionId={p.Id}&pageSize=1");
        Assert.NotEmpty(conflicts!.Items); Assert.True(conflicts.TotalItems > 0);
        var exclusions = await Write(client, HttpMethod.Put, $"/{p.Id}/exclusions", new { expectedVersion = p.Version, productIds = Array.Empty<Guid>(), variantIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.OK, exclusions.StatusCode);
        var get = await client.GetAsync(Api + $"/{p.Id}/exclusions"); Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Api + $"/{p.Id}/variants")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Api + "/conflicts?pageSize=101")).StatusCode);
        app.AuthUnavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(Api + "/conflicts")).StatusCode);
    }

    [PostgresFact]
    public async Task FullPostmanFlowAppliesPricesRepairsConflictsExcludesEndsAndCoversSubcategories()
    {
        await using var app = await CatalogManagementHttpTests.OfferApp.Create();
        await using var db = CatalogManagementPostgresTests.Open();
        // Explicit, isolated fixture: no competing campaigns, known prices 50/110/120.
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(2, 2), new(1, 1)), default);
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/web/auth/login", new { email = "admin@example.test", password = "test" })).StatusCode);

        async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"HTTP {(int)response.StatusCode}: {text}");
            Assert.True(response.Headers.CacheControl?.NoStore);
            return System.Text.Json.JsonSerializer.Deserialize<T>(text, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        }
        async Task<PromotionResponse> Current(Guid id) => await Read<PromotionResponse>(await client.GetAsync(Api + $"/{id}"));
        async Task<CurrentVariantPriceResponse> Price(Guid variantId) => await Read<CurrentVariantPriceResponse>(await client.GetAsync($"/api/v1/web/admin/variants/{variantId}/price"));
        async Task End(Guid id)
        {
            var current = await Current(id);
            var ended = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, $"/{id}/end", new { expectedVersion = current.Version }));
            Assert.Equal("Ended", ended.Promotion.Status); Assert.Equal(0, ended.Promotion.ActiveVariants);
        }

        var created = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, "",
            new { scope = "catalog", name = "Postman -60 zł", discountType = "amount", discountValue = 60 }), HttpStatusCode.Created);
        var id = created.Promotion.Id;
        var preview = await Read<PromotionPreviewResponse>(await Write(client, HttpMethod.Post, $"/{id}/preview", new { }));
        Assert.True(preview.CanActivate); Assert.Equal(1, preview.EligibleVariants); Assert.Equal(2, preview.ConflictedVariants);
        var blue = preview.Items.Single(v => v.Sku == "DEMO-TSHIRT-BLUE-M").VariantId;
        var cap = preview.Items.Single(v => v.Sku == "DEMO-CAP-ONE").VariantId;
        var activationKey = Guid.NewGuid();
        var activationBody = new { expectedVersion = preview.Version, expectedScopeToken = preview.ScopeToken };
        var activated = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, $"/{id}/activate", activationBody, activationKey));
        Assert.Equal("Active", activated.Promotion.Status); Assert.Equal(1, activated.Promotion.ActiveVariants);
        Assert.Equal(50, (await Price(blue)).Price!.SellingAmount); // 110 - 60
        var capBefore = (await Price(cap)).Price!;
        Assert.Equal(50, capBefore.SellingAmount); // Invalid 50 - 60 is not applied.
        var conflicts = await Read<PromotionConflictPage>(await client.GetAsync(Api + $"/conflicts?promotionId={id}"));
        Assert.Contains(conflicts.Items, c => c.VariantId == cap && c.Code == "invalid_discount");

        using var priceRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/web/admin/variants/{cap}/price-changes")
        { Content = JsonContent.Create(new { baseAmount = 80, currency = "PLN", reason = "Postman: poprawka", expectedPriceChangeId = capBefore.Id }) };
        priceRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        await Read<ChangeVariantPriceResponse>(await client.SendAsync(priceRequest), HttpStatusCode.Created);
        Assert.Equal(20, (await Price(cap)).Price!.SellingAmount); // Automatic 80 - 60
        var repaired = await Read<PromotionConflictPage>(await client.GetAsync(Api + $"/conflicts?promotionId={id}"));
        Assert.DoesNotContain(repaired.Items, c => c.VariantId == cap);

        var competing = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, "",
            new { scope = "catalog", name = "Konkurująca", discountType = "percentage", discountValue = 90 }), HttpStatusCode.Created);
        var competingPreview = await Read<PromotionPreviewResponse>(await Write(client, HttpMethod.Post, $"/{competing.Promotion.Id}/preview", new { }));
        Assert.False(competingPreview.CanActivate);
        var rejected = await Read<PromotionError>(await Write(client, HttpMethod.Post, $"/{competing.Promotion.Id}/activate",
            new { expectedVersion = competingPreview.Version, expectedScopeToken = competingPreview.ScopeToken }), HttpStatusCode.Conflict);
        Assert.Equal("active_promotion", rejected.Code);

        var current = await Current(id);
        await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Put, $"/{id}/exclusions",
            new { expectedVersion = current.Version, productIds = Array.Empty<Guid>(), variantIds = new[] { cap } }));
        Assert.Equal(80, (await Price(cap)).Price!.SellingAmount);
        var members = await Read<PromotionMemberPage>(await client.GetAsync(Api + $"/{id}/variants"));
        Assert.Contains(members.Items, m => m.VariantId == cap && m.EndedAt.HasValue);
        await End(id);
        Assert.Equal(110, (await Price(blue)).Price!.SellingAmount);
        var replay = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, $"/{id}/activate", activationBody, activationKey));
        Assert.True(replay.Replayed); Assert.Equal("Ended", (await Current(id)).Status);

        var clothing = await db.Categories.Where(c => c.Slug == "demo-odziez").Select(c => c.Id).SingleAsync();
        var category = await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, "",
            new { scope = "category", categoryId = clothing, name = "Poddrzewo -90%", discountType = "percentage", discountValue = 90 }), HttpStatusCode.Created);
        var categoryPreview = await Read<PromotionPreviewResponse>(await Write(client, HttpMethod.Post, $"/{category.Promotion.Id}/preview", new { }));
        Assert.Contains(categoryPreview.Items, v => v.VariantId == blue); // Assigned to child category, not the selected root.
        Assert.DoesNotContain(categoryPreview.Items, v => v.VariantId == cap);
        await Read<PromotionCommandResponse>(await Write(client, HttpMethod.Post, $"/{category.Promotion.Id}/activate",
            new { expectedVersion = categoryPreview.Version, expectedScopeToken = categoryPreview.ScopeToken }));
        Assert.Equal(11, (await Price(blue)).Price!.SellingAmount);
        await End(category.Promotion.Id);
        Assert.Equal(110, (await Price(blue)).Price!.SellingAmount);
    }
}
