using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Shopping;
using E_commerce_backend.Services.Catalog;
using E_commerce_backend.Services.Pricing;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Tests.Integration;

[Collection("Postgres catalog")]
public sealed class ShoppingPostgresTests
{
    [PostgresFact]
    public async Task CollectionPromotionIsIdenticalInCartFavoritesAndCatalogWithoutAnotherDiscount()
    {
        await using var s = await ShoppingScenario.Create();
        var cart = await s.Cart(s.AccountA);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, 2, Stamp(cart))).Status);
        var favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Cap, Stamp(favorites))).Status);
        await using var db = ShoppingScenario.Open();
        var collections = new CollectionService(db, TimeProvider.System);
        var created = Assert.IsType<CollectionCommandResponse>((await collections.MutateAsync("create", Guid.Empty,
            new() { DisplayName = "Shopping discount", Slug = "shopping-discount" }, null, Guid.NewGuid(), s.UserA, default)).Body).Collection;
        var members = Assert.IsType<CollectionCommandResponse>((await collections.MutateAsync("members", created.Id, null,
            new() { ExpectedVersion = created.Version, ProductIds = [await db.ProductVariants.Where(v => v.Id == s.Cap).Select(v => v.ProductId).SingleAsync()] },
            Guid.NewGuid(), s.UserA, default)).Body).Collection;
        Assert.Equal(200, (await collections.MutateAsync("update", created.Id, new() { ExpectedVersion = members.Version,
            DisplayName = created.DisplayName, Slug = created.Slug, Status = "Published" }, null, Guid.NewGuid(), s.UserA, default)).Status);
        var promotions = new PromotionService(db, TimeProvider.System);
        var promo = Assert.IsType<PromotionCommandResponse>((await promotions.MutateAsync("create", Guid.Empty,
            new() { Scope = "collection", CollectionId = created.Id, Name = "Shopping -20%", DiscountType = "percentage", DiscountValue = 20 },
            null, Guid.NewGuid(), s.UserA, default)).Body).Promotion;
        var preview = Assert.IsType<PromotionPreviewResponse>((await promotions.PreviewAsync(promo.Id, 1, 20, default)).Body);
        Assert.True(preview.CanActivate);
        Assert.Equal(200, (await promotions.MutateAsync("activate", promo.Id, null, new PromotionActivationRequest
        {
            ExpectedVersion = preview.Version, ExpectedCollectionVersion = preview.CollectionVersion, Variants = preview.ExpectedPrices.ToList()
        }, Guid.NewGuid(), s.UserA, default)).Status);
        cart = await s.Cart(s.AccountA);
        var price = Assert.Single(cart.Items).Variant!.Price;
        Assert.Equal(40m, price.Amount); Assert.Equal(80m, cart.GoodsSubtotal); Assert.NotNull(price.Reduction);
        Assert.Equal(price, Assert.Single((await s.Favorites(s.UserA)).Items).Variant!.Price);
        var capProduct = await db.ProductVariants.Where(v => v.Id == s.Cap).Select(v => v.ProductId).SingleAsync();
        var details = Assert.IsType<ProductDetailsResponse>(Assert.IsType<OkObjectResult>(await new ProductsController(db).Get(capProduct, null, default)).Value);
        Assert.Equal(price, Assert.Single(Assert.Single(details.Variants).CurrentPrices));
        Assert.Equal(200, (await promotions.MutateAsync("end", promo.Id, null,
            new() { ExpectedVersion = 2 }, Guid.NewGuid(), s.UserA, default)).Status);
        Assert.Equal(100m, (await s.Cart(s.AccountA)).GoodsSubtotal);
        Assert.Equal(cart.Version, (await s.Cart(s.AccountA)).Version);
    }

    [PostgresFact]
    public async Task CatalogResetRacingWithCartAndFavoritesWritesLeavesNoStaleItems()
    {
        await using var s = await ShoppingScenario.Create();
        var cart = await s.Cart(s.AccountA);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, 1, Stamp(cart))).Status);
        cart = await s.Cart(s.AccountA);
        var favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Red, Stamp(favorites))).Status);
        favorites = await s.Favorites(s.UserA);
        await using var db = ShoppingScenario.Open();
        var reset = new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(1, 1)), default);
        var cartWrite = s.SetCart(s.AccountA, s.Cap, 2, Stamp(cart));
        var favoriteWrite = s.SetFavorite(s.UserA, s.Blue, Stamp(favorites));
        await Task.WhenAll(reset, cartWrite, favoriteWrite);
        Assert.Contains((await cartWrite).Status, new[] { 200, 409 });
        Assert.Contains((await favoriteWrite).Status, new[] { 200, 409 });
        Assert.Empty((await s.Cart(s.AccountA)).Items);
        Assert.Empty((await s.Favorites(s.UserA)).Items);
    }

    [PostgresFact]
    public async Task CartAndFavoritesKeepOwnersAndVariantsSeparateWithoutProfileOrMerging()
    {
        await using var s = await ShoppingScenario.Create();
        var guest = await s.Guest();
        var g = await s.Cart(guest);
        var a = await s.Cart(s.AccountA);
        var b = await s.Cart(s.AccountB);
        Assert.NotEqual(g.ScopeId, a.ScopeId); Assert.NotEqual(a.ScopeId, b.ScopeId);
        Assert.Equal(200, (await s.SetCart(guest, s.Red, 2, Stamp(g))).Status);
        Assert.Empty((await s.Cart(s.AccountA)).Items);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, 1, Stamp(a))).Status);
        Assert.Equal(s.Red, Assert.Single((await s.Cart(guest)).Items).VariantId);
        Assert.Equal(s.Cap, Assert.Single((await s.Cart(s.AccountA)).Items).VariantId);
        Assert.Empty((await s.Cart(s.AccountB)).Items);
        // Versions coincide, but a delayed guest write must not target the account.
        var currentGuest = await s.Cart(guest);
        Assert.Equal((await s.Cart(s.AccountA)).Version, currentGuest.Version);
        Assert.Equal(409, (await s.SetCart(s.AccountA, s.Red, 3, Stamp(currentGuest))).Status);
        var favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Red, Stamp(favorites))).Status);
        favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Blue, Stamp(favorites))).Status);
        favorites = await s.Favorites(s.UserA);
        Assert.Equal(2, favorites.TotalItems);
        Assert.Equal(new[] { s.Red, s.Blue }.Order(), favorites.Items.Select(i => i.VariantId).Order());
        Assert.All(favorites.Items, i => Assert.Equal(s.Shirt, i.Variant!.ProductId));
        Assert.NotEqual(favorites.Items[0].Variant!.Price.Amount, favorites.Items[1].Variant!.Price.Amount);
        var replay = await s.SetFavorite(s.UserA, s.Red, Stamp(favorites));
        Assert.Equal(favorites.Version, Assert.IsType<ShoppingVersionResponse>(replay.Body).Version);
        Assert.Empty((await s.Favorites(s.UserB)).Items);
        await using var db = ShoppingScenario.Open();
        Assert.False(await db.UserProfiles.AnyAsync(p => p.UserId == s.UserA));
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id={s.UserA}");
        Assert.False(await db.ShoppingStates.AnyAsync(c => c.UserId == s.UserA));
        Assert.False(await db.FavoriteLists.AnyAsync(f => f.UserId == s.UserA));
        Assert.False(await db.FavoriteItems.AnyAsync(f => f.UserId == s.UserA));
        Assert.Single((await s.Cart(guest)).Items);
    }

    [PostgresFact]
    public async Task CartUsesLivePricesAvailabilityAndRedactedPlaceholdersWithoutReservations()
    {
        await using var s = await ShoppingScenario.Create();
        var cart = await s.Cart(s.AccountA);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, 2, Stamp(cart))).Status);
        cart = await s.Cart(s.AccountA);
        Assert.Equal(100m, cart.GoodsSubtotal); Assert.True(cart.CanCheckout);
        await using var db = ShoppingScenario.Open();
        Assert.Equal(3, await db.VariantStocks.Where(x => x.VariantId == s.Cap).Select(x => x.QuantityOnHand).SingleAsync());
        var now = DateTime.UtcNow;
        db.VariantPriceChanges.Add(new() { VariantId = s.Cap, BaseAmount = 70, SellingAmount = 70, EffectiveAt = now, CreatedAt = now, Reason = "Shopping test" });
        db.VariantPriceChanges.Add(new() { VariantId = s.Cap, BaseAmount = 999, SellingAmount = 999, EffectiveAt = now.AddDays(1), CreatedAt = now, Reason = "Future test" });
        await db.SaveChangesAsync();
        cart = await s.Cart(s.AccountA);
        Assert.Equal(140m, cart.GoodsSubtotal); Assert.Equal(1, cart.Version);
        await db.VariantStocks.Where(x => x.VariantId == s.Cap).ExecuteUpdateAsync(x => x.SetProperty(v => v.QuantityOnHand, 1));
        cart = await s.Cart(s.AccountA);
        Assert.Equal("insufficient_stock", Assert.Single(cart.Items).Availability);
        Assert.Equal(0m, cart.GoodsSubtotal); Assert.Equal(1, cart.BlockedItems); Assert.False(cart.CanCheckout);
        Assert.Equal(409, (await s.SetCart(s.AccountA, s.Cap, 3, Stamp(cart))).Status);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, 1, Stamp(cart))).Status);
        await db.VariantStocks.Where(x => x.VariantId == s.Cap).ExecuteDeleteAsync();
        cart = await s.Cart(s.AccountA);
        Assert.Equal("stock_unknown", Assert.Single(cart.Items).Availability);
        var favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Cap, Stamp(favorites))).Status);
        await db.Products.Where(p => p.Id == s.Shirt).ExecuteUpdateAsync(p => p.SetProperty(v => v.Status, CatalogStatus.Hidden));
        Assert.Equal(404, (await s.SetCart(s.AccountA, s.Red, 1, Stamp(cart))).Status);
        // The only variant cannot be hidden while its parent stays Published.
        var capProductId = await db.ProductVariants.Where(v => v.Id == s.Cap).Select(v => v.ProductId).SingleAsync();
        await db.Products.Where(p => p.Id == capProductId).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, CatalogStatus.Hidden));
        await db.ProductVariants.Where(v => v.Id == s.Cap).ExecuteUpdateAsync(v => v.SetProperty(x => x.Status, CatalogStatus.Hidden));
        cart = await s.Cart(s.AccountA);
        Assert.Equal("unavailable", Assert.Single(cart.Items).Availability); Assert.Null(cart.Items[0].Variant);
        Assert.Null(Assert.Single((await s.Favorites(s.UserA)).Items).Variant);
        Assert.Equal(200, (await s.SetCart(s.AccountA, s.Cap, null, Stamp(cart))).Status);
        favorites = await s.Favorites(s.UserA);
        Assert.Equal(200, (await s.SetFavorite(s.UserA, s.Cap, Stamp(favorites), false)).Status);
        Assert.Empty((await s.Cart(s.AccountA)).Items); Assert.Empty((await s.Favorites(s.UserA)).Items);
    }

    [PostgresFact]
    public async Task ConcurrentWritesHaveOneWinnerAndRetriesCannotIncreaseQuantity()
    {
        await using var s = await ShoppingScenario.Create();
        var cart = await s.Cart(s.AccountA);
        var results = await Task.WhenAll(s.SetCart(s.AccountA, s.Red, 2, Stamp(cart)), s.SetCart(s.AccountA, s.Blue, 2, Stamp(cart)));
        Assert.Single(results, r => r.Status == 200); Assert.Single(results, r => r.Status == 409);
        var row = Assert.Single((await s.Cart(s.AccountA)).Items);
        Assert.Equal(2, row.Quantity);
        Assert.Equal(409, (await s.SetCart(s.AccountA, row.VariantId, 2, Stamp(cart))).Status);
        var favorites = await s.Favorites(s.UserA);
        var writes = await Task.WhenAll(s.SetFavorite(s.UserA, s.Red, Stamp(favorites)), s.SetFavorite(s.UserA, s.Blue, Stamp(favorites)));
        Assert.Single(writes, r => r.Status == 200); Assert.Single(writes, r => r.Status == 409);
        Assert.Single((await s.Favorites(s.UserA)).Items);
        // Simultaneous first reads must resolve to one account state/list.
        var carts = await Task.WhenAll(s.Cart(s.AccountB), s.Cart(s.AccountB));
        Assert.Equal(carts[0].ScopeId, carts[1].ScopeId);
        var lists = await Task.WhenAll(s.Favorites(s.UserB), s.Favorites(s.UserB));
        Assert.Equal(lists[0].ScopeId, lists[1].ScopeId);
    }

    [PostgresFact]
    public async Task GuestSessionDoesNotSlideExpiresAndCanBeRevoked()
    {
        await using var s = await ShoppingScenario.Create();
        await using var db = ShoppingScenario.Open();
        var service = new GuestShoppingSessionService(db, new ShoppingStore(db, TimeProvider.System), TimeProvider.System);
        var context = new DefaultHttpContext();
        var first = await service.CreateAsync(new(null, null), context.Response, default);
        Assert.Equal(201, first.Status);
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=lax", cookie);
        context.Request.Headers.Cookie = cookie.Split(';')[0];
        var hash = GuestShoppingCookie.ReadHash(context.Request)!;
        var owner = new ShoppingOwner(null, hash);
        var secondContext = new DefaultHttpContext();
        var second = await service.CreateAsync(owner, secondContext.Response, default);
        Assert.Equal(first.Body, second.Body); Assert.Empty(secondContext.Response.Headers.SetCookie.ToString());
        var cart = await s.Cart(owner);
        Assert.Equal(200, (await s.SetCart(owner, s.Cap, 1, Stamp(cart))).Status);
        await db.ShoppingStates.Where(x => x.Id == cart.ScopeId).ExecuteUpdateAsync(x => x.SetProperty(v => v.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Null((await s.Cart(owner)).ScopeId);
        Assert.Equal(409, (await s.SetCart(owner, s.Cap, 2, Stamp(cart))).Status);
        Assert.Equal(204, (await service.DeleteAsync(hash, secondContext.Response, default)).Status);
        Assert.False(await db.ShoppingStates.AnyAsync(x => x.Id == cart.ScopeId));
        Assert.Equal(409, (await service.CreateAsync(s.AccountA, secondContext.Response, default)).Status);
    }

    [PostgresFact]
    public async Task LimitsPaginationConstraintsAndResetAreEnforced()
    {
        await using var s = await ShoppingScenario.Create(520);
        var cart = await s.Cart(s.AccountA);
        var favorites = await s.Favorites(s.UserA);
        await using var db = ShoppingScenario.Open();
        await db.Database.ExecuteSqlRawAsync("UPDATE app.variant_stock SET quantity_on_hand=1000");
        var ids = await db.ProductVariants.Where(v => v.Status == CatalogStatus.Published && v.Product.Status == CatalogStatus.Published)
            .OrderBy(v => v.Id).Select(v => v.Id).Take(501).ToArrayAsync();
        Assert.Equal(501, ids.Length);
        for (var i = 0; i < 500; i++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.favorite_items(user_id,variant_id,created_at) VALUES ({s.UserA},{ids[i]},now())");
        for (var i = 0; i < 100; i++)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.cart_items(shopping_state_id,variant_id,quantity,created_at,updated_at) VALUES ({cart.ScopeId!.Value},{ids[i]},1,now(),now())");
        cart = await s.Cart(s.AccountA); favorites = await s.Favorites(s.UserA);
        Assert.Equal(409, (await s.SetCart(s.AccountA, ids[100], 1, Stamp(cart))).Status);
        Assert.Equal(409, (await s.SetFavorite(s.UserA, ids[500], Stamp(favorites))).Status);
        Assert.Equal(400, (await s.SetCart(s.AccountA, ids[0], 100, Stamp(cart))).Status);
        Assert.Equal(400, (await s.SetCart(s.AccountA, ids[0], 0, Stamp(cart))).Status);
        var page1 = await s.Favorites(s.UserA, 1, 50); var page2 = await s.Favorites(s.UserA, 2, 50);
        Assert.Equal(500, page1.TotalItems); Assert.Equal(10, page1.TotalPages);
        Assert.Empty(page1.Items.Select(x => x.VariantId).Intersect(page2.Items.Select(x => x.VariantId)));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE app.cart_items SET quantity=0 WHERE shopping_state_id={cart.ScopeId!.Value}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO app.shopping_states(id,cart_version,created_at,updated_at) VALUES (gen_random_uuid(),0,now(),now())"));
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            await db.Database.OpenConnectionAsync();
            command.CommandText = "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='app' AND c.relname IN ('shopping_states','cart_items','favorite_lists','favorite_items') AND c.relrowsecurity";
            Assert.Equal(4L, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT has_table_privilege('anon','app.cart_items','SELECT') OR has_table_privilege('authenticated','app.favorite_items','INSERT') OR has_table_privilege('service_role','app.shopping_states','SELECT')";
            Assert.Equal(false, await command.ExecuteScalarAsync());
        }
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
        var after = await s.Cart(s.AccountA); var favoritesAfter = await s.Favorites(s.UserA);
        Assert.Empty(after.Items); Assert.Equal(cart.ScopeId, after.ScopeId); Assert.True(after.Version > cart.Version);
        Assert.Empty(favoritesAfter.Items); Assert.Equal(favorites.ScopeId, favoritesAfter.ScopeId); Assert.True(favoritesAfter.Version > favorites.Version);
        Assert.Equal(409, (await s.SetCart(s.AccountA, ids[0], 1, Stamp(cart))).Status);
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(16, 16), new(2, 2)), default);
    }

    public static ShoppingVersionRequest Stamp(CartResponse c) => new() { ExpectedScopeId = c.ScopeId, ExpectedVersion = c.Version };
    public static ShoppingVersionRequest Stamp(FavoritesResponse f) => new() { ExpectedScopeId = f.ScopeId, ExpectedVersion = f.Version };
}

internal sealed class ShoppingScenario : IAsyncDisposable
{
    public Guid UserA { get; } = Guid.NewGuid();
    public Guid UserB { get; } = Guid.NewGuid();
    public ShoppingOwner AccountA => new(UserA, null);
    public ShoppingOwner AccountB => new(UserB, null);
    public Guid Red { get; private set; }
    public Guid Blue { get; private set; }
    public Guid Cap { get; private set; }
    public Guid Shirt { get; private set; }
    private readonly List<Guid> guests = [];
    public static AppDbContext Open()
    {
        var cs = Environment.GetEnvironmentVariable("ECOMMERCE_TEST_DATABASE")!;
        var address = new NpgsqlConnectionStringBuilder(cs);
        if (address.Host is not ("localhost" or "127.0.0.1") || address.Database?.StartsWith("ecomm_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Shopping tests require an isolated local ecomm_test_* database.");
        return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
    public static async Task<ShoppingScenario> Create(int products = 16)
    {
        var s = new ShoppingScenario();
        await using var db = Open();
        await new DemoCatalogSeeder(db).SeedAsync(new(new(3, 3), new(products, products), new(1, 1)), default);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO auth.users(id) VALUES ({s.UserA}),({s.UserB})");
        var variants = await db.ProductVariants.Where(v => v.SKU == "DEMO-TSHIRT-RED-S" || v.SKU == "DEMO-TSHIRT-BLUE-M" || v.SKU == "DEMO-CAP-ONE").ToArrayAsync();
        s.Red = variants.Single(v => v.SKU == "DEMO-TSHIRT-RED-S").Id;
        s.Blue = variants.Single(v => v.SKU == "DEMO-TSHIRT-BLUE-M").Id;
        s.Cap = variants.Single(v => v.SKU == "DEMO-CAP-ONE").Id;
        s.Shirt = variants.Single(v => v.Id == s.Red).ProductId;
        return s;
    }
    public async Task<ShoppingOwner> Guest()
    {
        var token = GuestShoppingCookie.Create();
        await using var db = Open();
        var state = new ShoppingState { GuestTokenHash = token.Hash, ExpiresAt = DateTime.UtcNow.AddDays(30), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.ShoppingStates.Add(state); await db.SaveChangesAsync(); guests.Add(state.Id);
        return new(null, token.Hash);
    }
    public static CartService CartService(AppDbContext db) => new(db, new ShoppingStore(db, TimeProvider.System), new ShoppingOfferReader(db), TimeProvider.System);
    public static FavoritesService FavoritesService(AppDbContext db) => new(db, new ShoppingStore(db, TimeProvider.System), new ShoppingOfferReader(db), TimeProvider.System);
    public async Task<CartResponse> Cart(ShoppingOwner owner)
    {
        await using var db = Open(); return Assert.IsType<CartResponse>((await CartService(db).GetAsync(owner, default)).Body);
    }
    public async Task<FavoritesResponse> Favorites(Guid user, int page = 1, int pageSize = 20)
    {
        await using var db = Open(); return Assert.IsType<FavoritesResponse>((await FavoritesService(db).GetAsync(user, page, pageSize, default)).Body);
    }
    public async Task<ShoppingResult> SetCart(ShoppingOwner owner, Guid id, int? quantity, ShoppingVersionRequest stamp)
    {
        await using var db = Open(); return await CartService(db).MutateAsync(owner, id, quantity, stamp, false, default);
    }
    public async Task<ShoppingResult> SetFavorite(Guid user, Guid id, ShoppingVersionRequest stamp, bool add = true)
    {
        await using var db = Open(); return await FavoritesService(db).MutateAsync(user, id, stamp, add, default);
    }
    public async ValueTask DisposeAsync()
    {
        await using var db = Open();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM auth.users WHERE id={UserA} OR id={UserB}");
        await db.ShoppingStates.Where(s => guests.Contains(s.Id)).ExecuteDeleteAsync();
    }
}
