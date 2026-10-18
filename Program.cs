using E_commerce_backend.Data;
using E_commerce_backend.Routing;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Profile;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabasePublishableKey = builder.Configuration["Supabase:PublishableKey"];
var databaseConnectionString = builder.Configuration.GetConnectionString("SupabaseDatabase");

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

builder.Services.AddHttpClient<SupabaseAuthClient>(client =>
{
    client.BaseAddress = new Uri(supabaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("apikey", supabasePublishableKey);
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
}

app.MapControllers();

app.MapGet("/", () => new { message = "API działa" });

app.Run();
