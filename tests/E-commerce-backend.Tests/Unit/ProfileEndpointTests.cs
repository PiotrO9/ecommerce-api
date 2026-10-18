using E_commerce_backend.Contracts.Profile;
using E_commerce_backend.Controllers;
using E_commerce_backend.Data;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace E_commerce_backend.Tests.Unit;

public sealed class ProfileEndpointTests
{
    [Fact]
    public async Task ProfileEndpoints_RequireSessionBeforeDatabaseAccess()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();
        using var httpClient = new HttpClient(new NeverCallHandler())
        { BaseAddress = new Uri("https://example.supabase.co/") };
        var auth = new AuthenticatedUserService(
            new SupabaseAuthClient(httpClient),
            new AuthCookieService(provider.GetRequiredService<IDataProtectionProvider>()));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=never_called;Username=postgres")
            .Options;
        await using var database = new AppDbContext(options);
        var context = new DefaultHttpContext();
        var controller = new ProfileController(auth, database)
        { ControllerContext = new ControllerContext { HttpContext = context } };

        Assert.IsType<UnauthorizedObjectResult>(await controller.GetMine(CancellationToken.None));

        var request = new UpdateMyProfileRequest { FirstName = "Jan", LastName = "Kowalski" };
        Assert.Equal(403, Assert.IsType<ObjectResult>(
            await controller.UpdateMine(request, null, CancellationToken.None)).StatusCode);

        Assert.IsType<UnauthorizedObjectResult>(
            await controller.UpdateMine(request, "XMLHttpRequest", CancellationToken.None));
    }

    private sealed class NeverCallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Supabase nie powinno być wywołane bez sesji.");
    }
}
