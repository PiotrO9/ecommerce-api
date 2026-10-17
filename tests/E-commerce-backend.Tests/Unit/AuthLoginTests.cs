using System.Net;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Auth;
using E_commerce_backend.Controllers;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace E_commerce_backend.Tests.Unit;

public sealed class AuthLoginTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 30)]
    public async Task Login_SetsProtectedCookieWithFixedDeadline(bool rememberMe, int days)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("E-commerce-backend");
        using var provider = services.BuildServiceProvider();
        var dataProtection = provider.GetRequiredService<IDataProtectionProvider>();
        var authCookieService = new AuthCookieService(dataProtection);

        using var httpClient = new HttpClient(new StubHandler("""
            {"access_token":"example-access-token","refresh_token":"example-refresh-token"}
            """)) { BaseAddress = new Uri("https://example.supabase.co/") };
        var controller = new AuthController(new SupabaseAuthClient(httpClient), authCookieService,
            new StubUserProfileReader())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var before = DateTimeOffset.UtcNow;
        var result = await controller.Login(new LoginRequest
        {
            Email = "user@example.com",
            Password = "password",
            RememberMe = rememberMe
        }, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        Assert.IsType<OkResult>(result);
        var header = controller.Response.Headers.SetCookie.ToString();
        Assert.StartsWith(AuthCookieService.CookieName + "=", header);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example-access-token", header);
        Assert.DoesNotContain("example-refresh-token", header);

        var protectedValue = header.Split(';')[0].Split('=', 2)[1];
        var plaintext = dataProtection.CreateProtector("ECommerce.AuthSession.v1")
            .Unprotect(protectedValue);
        var session = JsonSerializer.Deserialize<AuthSessionPayload>(plaintext);
        Assert.NotNull(session);
        Assert.Equal("example-access-token", session.AccessToken);
        Assert.Equal("example-refresh-token", session.RefreshToken);
        Assert.InRange(session.AbsoluteExpiresAt, before.AddDays(days), after.AddDays(days));
        Assert.Contains("expires=", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithoutBothTokens_DoesNotSetCookie()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();

        using var httpClient = new HttpClient(new StubHandler("""{"access_token":"only-one"}"""))
        { BaseAddress = new Uri("https://example.supabase.co/") };
        var controller = new AuthController(
            new SupabaseAuthClient(httpClient),
            new AuthCookieService(provider.GetRequiredService<IDataProtectionProvider>()),
            new StubUserProfileReader())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Login(new LoginRequest
        {
            Email = "user@example.com", Password = "password"
        }, CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(string.IsNullOrEmpty(controller.Response.Headers.SetCookie.ToString()));
    }

    [Fact]
    public async Task Login_MapsSupabaseInvalidCredentialsResponseTo401()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();

        using var httpClient = new HttpClient(new StubHandler("""
            {"code":400,"error_code":"invalid_credentials","msg":"Invalid login credentials"}
            """, HttpStatusCode.BadRequest))
        { BaseAddress = new Uri("https://example.supabase.co/") };
        var controller = new AuthController(
            new SupabaseAuthClient(httpClient),
            new AuthCookieService(provider.GetRequiredService<IDataProtectionProvider>()),
            new StubUserProfileReader())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Login(new LoginRequest
        {
            Email = "user@example.com", Password = "wrong-password"
        }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.True(string.IsNullOrEmpty(controller.Response.Headers.SetCookie.ToString()));
    }

    [Fact]
    public void OversizedSession_DoesNotSetCookie()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext();
        var service = new AuthCookieService(provider.GetRequiredService<IDataProtectionProvider>());

        var wasWritten = service.TryAppend(context.Response,
            new AuthSessionPayload(new string('a', 4000), "refresh", DateTimeOffset.UtcNow.AddDays(1)));

        Assert.False(wasWritten);
        Assert.True(string.IsNullOrEmpty(context.Response.Headers.SetCookie.ToString()));
    }

    [Fact]
    public void PersistedKeys_AllowReadingCookieAfterNewProviderStarts()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), "ecommerce-auth-test-" + Guid.NewGuid());
        Directory.CreateDirectory(keysPath);

        try
        {
            var firstServices = new ServiceCollection();
            firstServices.AddDataProtection()
                .SetApplicationName("E-commerce-backend")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

            string protectedValue;
            using (var firstProvider = firstServices.BuildServiceProvider())
            {
                var context = new DefaultHttpContext();
                var service = new AuthCookieService(
                    firstProvider.GetRequiredService<IDataProtectionProvider>());
                Assert.True(service.TryAppend(context.Response,
                    new AuthSessionPayload("access", "refresh", DateTimeOffset.UtcNow.AddDays(1))));
                protectedValue = context.Response.Headers.SetCookie.ToString().Split(';')[0].Split('=', 2)[1];
            }

            var secondServices = new ServiceCollection();
            secondServices.AddDataProtection()
                .SetApplicationName("E-commerce-backend")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            using var secondProvider = secondServices.BuildServiceProvider();
            var plaintext = secondProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("ECommerce.AuthSession.v1")
                .Unprotect(protectedValue);

            Assert.Contains("\"AccessToken\":\"access\"", plaintext);
        }
        finally
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }

    private sealed class StubHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
