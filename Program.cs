using E_commerce_backend.Data;
using E_commerce_backend.Routing;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Email;
using E_commerce_backend.Services.Profile;
using E_commerce_backend.Services.Shopping;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabasePublishableKey = builder.Configuration["Supabase:PublishableKey"];
var databaseConnectionString = builder.Configuration.GetConnectionString("SupabaseDatabase");
var supabaseSecretKey = builder.Configuration["Supabase:SecretKey"];
var mailtrapApiToken = builder.Configuration["Mailtrap:ApiToken"];
var mailtrapFromEmail = builder.Configuration["Mailtrap:FromEmail"];
var mailtrapFromName = string.IsNullOrWhiteSpace(builder.Configuration["Mailtrap:FromName"])
    ? "E-commerce"
    : builder.Configuration["Mailtrap:FromName"]!;
var passwordResetUrl = builder.Configuration["PasswordReset:Url"];

if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    throw new InvalidOperationException(
        "Brakuje ustawienia Supabase:Url.");
}

if (string.IsNullOrWhiteSpace(supabasePublishableKey))
{
    throw new InvalidOperationException(
        "Brakuje ustawienia Supabase:PublishableKey.");
}

if (string.IsNullOrWhiteSpace(databaseConnectionString))
{
    throw new InvalidOperationException(
        "Brakuje ustawienia ConnectionStrings:SupabaseDatabase.");
}

if (string.IsNullOrWhiteSpace(supabaseSecretKey) ||
    string.IsNullOrWhiteSpace(mailtrapApiToken) ||
    string.IsNullOrWhiteSpace(mailtrapFromEmail) ||
    !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(mailtrapFromEmail) ||
    !Uri.TryCreate(passwordResetUrl, UriKind.Absolute, out var resetUri) ||
    resetUri.Scheme != Uri.UriSchemeHttps ||
    !string.IsNullOrEmpty(resetUri.Fragment))
{
    throw new InvalidOperationException(
        "Skonfiguruj Supabase:SecretKey, Mailtrap:ApiToken, Mailtrap:FromEmail oraz PasswordReset:Url (HTTPS, bez fragmentu).");
}

builder.Services.AddHttpClient<SupabaseAuthClient>(client =>
{
    client.BaseAddress = new Uri(supabaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabasePublishableKey);
});
builder.Services.AddHttpClient<SupabasePasswordResetClient>(client =>
{
    client.BaseAddress = new Uri(supabaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabaseSecretKey);
});
builder.Services.AddHttpClient<IEmailSender, MailtrapEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://send.api.mailtrap.io/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mailtrapApiToken);
});
builder.Services.AddSingleton(new PasswordResetOptions(resetUri, mailtrapFromEmail, mailtrapFromName));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("shopping-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Shopping:WriteRequestsPerMinute", 120),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    options.AddPolicy("shopping-guest-create", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Shopping:GuestSessionsPerHour", 10),
            Window = TimeSpan.FromHours(1), QueueLimit = 0
        }));
    options.OnRejected = async (rejection, ct) =>
    {
        var path = rejection.HttpContext.Request.Path;
        if (path.StartsWithSegments("/api/v1/web/cart") || path.StartsWithSegments("/api/v1/web/favorites") || path.StartsWithSegments("/api/v1/web/shopping") || path.StartsWithSegments("/api/v1/web/orders"))
        {
            rejection.HttpContext.Response.Headers.CacheControl = "no-store";
            await rejection.HttpContext.Response.WriteAsJsonAsync(
                new E_commerce_backend.Contracts.Shopping.ShoppingError("shopping_rate_limited", "Spróbuj ponownie później."), ct);
        }
    };
    options.AddPolicy("password-reset-request", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("password-reset-confirm", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(databaseConnectionString,
        npgsql => npgsql.MigrationsHistoryTable("__EcommerceMigrationsHistory", "public")));

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("E-commerce-backend");

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddSingleton<AuthCookieService>();
builder.Services.AddScoped<AuthenticatedUserService>();
builder.Services.AddScoped<IAdministratorAccess, AdministratorAccess>();
builder.Services.AddScoped<IUserProfileReader, UserProfileReader>();
builder.Services.AddScoped<DemoCatalogSeeder>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<E_commerce_backend.Services.Pricing.VariantPriceService>();
builder.Services.AddScoped<E_commerce_backend.Services.Pricing.PromotionService>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.CollectionService>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.CatalogCommandStore>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.CatalogManagementReader>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.ProductManagementService>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.CategoryManagementService>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.AttributeManagementService>();
builder.Services.AddScoped<E_commerce_backend.Services.Catalog.Management.VariantStockService>();
builder.Services.AddScoped<ShoppingOwnerResolver>();
builder.Services.AddScoped<ShoppingStore>();
builder.Services.AddScoped<ShoppingOfferReader>();
builder.Services.AddScoped<GuestShoppingSessionService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<FavoritesService>();
builder.Services.AddOptions<E_commerce_backend.Contracts.Inventory.InventoryOptions>()
    .BindConfiguration("Inventory")
    .Validate(o => o.ReservationMinutes is >= 1 and <= 120 && o.ExpiryIntervalSeconds is >= 1 and <= 300,
        "Inventory: ReservationMinutes 1–120, ExpiryIntervalSeconds 1–300.")
    .ValidateOnStart();
builder.Services.AddScoped<E_commerce_backend.Services.Inventory.InventoryOrderService>();
builder.Services.AddHostedService<E_commerce_backend.Services.Inventory.ReservationExpiryWorker>();
builder.Services.AddHostedService<E_commerce_backend.Services.Pricing.PromotionReconciliationWorker>();

builder.Services.AddControllers(options =>
    options.Conventions.Add(new RouteTokenTransformerConvention(
        new LowercaseRouteTokenTransformer())));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "E-commerce API v1"));

    if (builder.Configuration.GetValue<bool>("DemoSeed:Enabled"))
    {
        var demoSeedKey = builder.Configuration["DemoSeed:Key"];
        if (string.IsNullOrWhiteSpace(demoSeedKey) || demoSeedKey.Length < 32)
        {
            throw new InvalidOperationException("DemoSeed:Key musi mieć co najmniej 32 znaki, gdy seeder jest włączony.");
        }

        app.MapPost("/api/v1/web/seed", async (
            [FromHeader(Name = "X-Demo-Seed-Key")] string? suppliedKey,
            HttpContext context, DemoCatalogSeeder seeder, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!DemoSeedAccess.IsAuthorized(suppliedKey, demoSeedKey))
            {
                return Results.NotFound();
            }

            DemoSeedRequest? request = null;
            if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            {
                if (!context.Request.HasJsonContentType())
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                try
                {
                    request = await context.Request.ReadFromJsonAsync<DemoSeedRequest>(cancellationToken);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Niepoprawny JSON." });
                }
            }

            if (!DemoSeedOptions.TryCreate(request, out var options, out var error))
            {
                return Results.BadRequest(new { error });
            }

            try { return Results.Ok(await seeder.SeedAsync(options!, cancellationToken)); }
            catch (InventorySeedBlockedException)
            {
                return Results.Conflict(new { code = "seed_blocked_by_orders", message = "Reset katalogu jest zablokowany, ponieważ istnieją zamówienia." });
            }
        });
    }
}

// Also covers routing/model-binding rejections (e.g. 415), which never reach a controller.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api/v1/web/cart") || path.StartsWithSegments("/api/v1/web/favorites") || path.StartsWithSegments("/api/v1/web/shopping") || path.StartsWithSegments("/api/v1/web/orders"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseRouting();
app.UseRateLimiter();
app.MapControllers();

app.MapGet("/", () => new { message = "API działa" });

app.Run();

public partial class Program { }
