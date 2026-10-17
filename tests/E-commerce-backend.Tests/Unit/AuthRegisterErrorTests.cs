using System.Net;
using System.Text;
using E_commerce_backend.Contracts.Auth;
using E_commerce_backend.Controllers;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace E_commerce_backend.Tests.Unit;

public sealed class AuthRegisterErrorTests
{
    [Fact]
    public async Task Register_MapsSupabaseWeakPasswordResponseTo400()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        using var provider = services.BuildServiceProvider();
        using var httpClient = new HttpClient(new StubHandler())
        { BaseAddress = new Uri("https://example.supabase.co/") };
        var controller = new AuthController(
            new SupabaseAuthClient(httpClient),
            new AuthCookieService(provider.GetRequiredService<IDataProtectionProvider>()),
            new StubUserProfileReader())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Register(new RegisterRequest
        {
            Email = "user@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""
                    {"code":400,"error_code":"weak_password","msg":"Password is weak"}
                    """, Encoding.UTF8, "application/json")
            });
        }
    }
}
