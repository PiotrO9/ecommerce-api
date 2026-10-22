using E_commerce_backend.Data;
using E_commerce_backend.Routing;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Development;
using E_commerce_backend.Services.Email;
using E_commerce_backend.Services.Profile;
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
builder.Services.AddScoped<IUserProfileReader, UserProfileReader>();
builder.Services.AddScoped<DemoCatalogSeeder>();

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

            var result = await seeder.SeedAsync(options!, cancellationToken);
            return Results.Ok(result);
        });
    }
}

app.UseRouting();
app.UseRateLimiter();
app.MapControllers();

app.MapGet("/", () => new { message = "API działa" });

app.Run();
