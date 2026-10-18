using System.Net;
using System.Text;
using System.Collections.Concurrent;
using E_commerce_backend.Controllers;
using E_commerce_backend.Services.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace E_commerce_backend.Tests.Unit;

public sealed class AuthEndpointTests
{
    [Fact]
    public async Task Me_WithoutCookie_Returns401WithoutCallingSupabase()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        using var fixture = new Fixture(handler);

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, handler.CallCount);
        Assert.Null(fixture.ProfileReader.RequestedUserId);
    }

    [Fact]
    public async Task Me_WithValidCookieAndNoProfile_ReturnsNullNames()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(HttpStatusCode.OK,
            $"{{\"id\":\"{userId}\",\"email\":\"user@example.com\",\"role\":\"admin\"}}");
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains(userId.ToString(), json);
        Assert.Contains("user@example.com", json);
        Assert.Contains("\"firstName\":null", json);
        Assert.Contains("\"lastName\":null", json);
        Assert.DoesNotContain("admin", json);
        Assert.Equal(userId, fixture.ProfileReader.RequestedUserId);
        Assert.Equal("auth/v1/user", handler.LastPath);
        Assert.Equal("Bearer", handler.LastAuthorizationScheme);
        Assert.Equal("access", handler.LastAuthorizationParameter);
    }

    [Fact]
    public async Task Me_WithSavedProfile_ReturnsNamesForAuthenticatedUser()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(HttpStatusCode.OK,
            $"{{\"id\":\"{userId}\",\"email\":\"user@example.com\"}}");
        using var fixture = new Fixture(handler);
        fixture.ProfileReader.Profile = new()
        {
            UserId = userId,
            FirstName = "Jan",
            LastName = "Kowalski"
        };
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"firstName\":\"Jan\"", json);
        Assert.Contains("\"lastName\":\"Kowalski\"", json);
        Assert.Equal(userId, fixture.ProfileReader.RequestedUserId);
    }

    [Fact]
    public async Task Me_DoesNotReturnAnotherUsersProfile()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(HttpStatusCode.OK,
            $"{{\"id\":\"{userId}\",\"email\":\"user@example.com\"}}");
        using var fixture = new Fixture(handler);
        fixture.ProfileReader.Profile = new()
        {
            UserId = Guid.NewGuid(),
            FirstName = "Inna",
            LastName = "Osoba"
        };
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"firstName\":null", json);
        Assert.Contains("\"lastName\":null", json);
        Assert.DoesNotContain("Inna", json);
        Assert.Equal(userId, fixture.ProfileReader.RequestedUserId);
    }

    [Fact]
    public async Task Me_WithExpiredCookie_Returns401WithoutCallingSupabase()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Me_WithTamperedCookie_Returns401WithoutCallingSupabase()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        using var fixture = new Fixture(handler);
        fixture.Context.Request.Headers.Cookie = AuthCookieService.CookieName + "=invalid";

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Me_WhenAccessTokenExpires_RefreshesBothTokensWithoutExtendingDeadline()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh"}"""),
            (HttpStatusCode.OK, $"{{\"id\":\"{userId}\",\"email\":\"user@example.com\"}}"));
        using var fixture = new Fixture(handler);
        var deadline = DateTimeOffset.UtcNow.AddDays(30);
        fixture.SetCookie(deadline);

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal("auth/v1/user", handler.RequestPaths[0]);
        Assert.Equal("auth/v1/token?grant_type=refresh_token", handler.RequestPaths[1]);
        Assert.Equal("auth/v1/user", handler.RequestPaths[2]);
        Assert.Equal("{\"refresh_token\":\"refresh\"}", handler.RefreshRequestBody);
        Assert.Equal("new-access", handler.LastAuthorizationParameter);
        var refreshed = fixture.ReadResponseCookie();
        Assert.NotNull(refreshed);
        Assert.Equal("new-access", refreshed.AccessToken);
        Assert.Equal("new-refresh", refreshed.RefreshToken);
        Assert.Equal(deadline, refreshed.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task Me_WhenRefreshTokenIsInvalid_ClearsCookieAndReturns401()
    {
        var handler = new StubHandler(
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.BadRequest,
                """{"code":400,"error_code":"refresh_token_not_found","msg":"Invalid refresh token"}"""));
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("expires=", fixture.Context.Response.Headers.SetCookie.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_WhenRefreshServiceIsUnavailable_KeepsCookieForRetry()
    {
        var handler = new StubHandler(
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.ServiceUnavailable, "{}"));
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var result = await fixture.Controller.Me(CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(string.IsNullOrEmpty(fixture.Context.Response.Headers.SetCookie.ToString()));
    }

    [Fact]
    public async Task Logout_RequiresCustomHeaderAndRevokesOnlyCurrentSession()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, "");
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));

        var rejected = await fixture.Controller.Logout(null, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(rejected).StatusCode);
        Assert.Equal(0, handler.CallCount);

        var result = await fixture.Controller.Logout("XMLHttpRequest", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("auth/v1/logout?scope=local", handler.LastPath);
        Assert.Equal("access", handler.LastAuthorizationParameter);
        Assert.Contains("expires=", fixture.Context.Response.Headers.SetCookie.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_WhenSupabaseUnavailable_KeepsCookieForRetry()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "{}");
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));
        var result = await fixture.Controller.Logout("XMLHttpRequest", CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.True(string.IsNullOrEmpty(fixture.Context.Response.Headers.SetCookie.ToString()));
    }

    [Fact]
    public async Task Logout_WithExpiredAccessToken_RefreshesBeforeRevokingSession()
    {
        var handler = new StubHandler(
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh"}"""),
            (HttpStatusCode.NoContent, ""));
        using var fixture = new Fixture(handler);
        fixture.SetCookie(DateTimeOffset.UtcNow.AddDays(1));
        var result = await fixture.Controller.Logout("XMLHttpRequest", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal("auth/v1/logout?scope=local", handler.RequestPaths[2]);
        Assert.Equal("new-access", handler.LastAuthorizationParameter);
        Assert.Single(fixture.Context.Response.Headers.SetCookie);
        Assert.Contains("expires=", fixture.Context.Response.Headers.SetCookie.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_TwoConcurrentRefreshes_ReturnUsableCookiesWithOriginalDeadline()
    {
        using var fixture = new Fixture(new ConcurrentRefreshHandler());
        var deadline = DateTimeOffset.UtcNow.AddDays(1);
        fixture.SetCookie(deadline);
        var secondContext = new DefaultHttpContext();
        secondContext.Request.Headers.Cookie = fixture.Context.Request.Headers.Cookie;
        var secondController = fixture.CreateController(secondContext);

        var first = fixture.Controller.Me(CancellationToken.None);
        var second = secondController.Me(CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.IsType<OkObjectResult>(result));
        foreach (var context in new[] { fixture.Context, secondContext })
        {
            var refreshed = fixture.ReadResponseCookie(context);
            Assert.NotNull(refreshed);
            Assert.Equal("new-access", refreshed.AccessToken);
            Assert.Equal("new-refresh", refreshed.RefreshToken);
            Assert.Equal(deadline, refreshed.AbsoluteExpiresAt);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _httpClient;
        private readonly AuthCookieService _cookieService;

        public DefaultHttpContext Context { get; } = new();
        public AuthController Controller { get; }
        public StubUserProfileReader ProfileReader { get; } = new();

        public Fixture(HttpMessageHandler handler)
        {
            var services = new ServiceCollection();
            services.AddDataProtection();
            _provider = services.BuildServiceProvider();
            _cookieService = new AuthCookieService(
                _provider.GetRequiredService<IDataProtectionProvider>());
            _httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.supabase.co/") };
            Controller = CreateController(Context);
        }

        public AuthController CreateController(DefaultHttpContext context)
        {
            return new AuthController(new SupabaseAuthClient(_httpClient), _cookieService, ProfileReader)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        public void SetCookie(DateTimeOffset expiresAt)
        {
            var cookieContext = new DefaultHttpContext();
            Assert.True(_cookieService.TryAppend(cookieContext.Response,
                new AuthSessionPayload("access", "refresh", expiresAt)));
            Context.Request.Headers.Cookie = cookieContext.Response.Headers.SetCookie.ToString().Split(';')[0];
        }

        public AuthSessionPayload? ReadResponseCookie(DefaultHttpContext? responseContext = null)
        {
            var cookieContext = new DefaultHttpContext();
            cookieContext.Request.Headers.Cookie =
                (responseContext ?? Context).Response.Headers.SetCookie[0]?.Split(';')[0];
            return _cookieService.Read(cookieContext.Request);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<(HttpStatusCode Status, string Json)> _responses;

        public StubHandler(HttpStatusCode status, string json) : this((status, json)) { }

        public StubHandler(params (HttpStatusCode Status, string Json)[] responses)
        {
            _responses = new ConcurrentQueue<(HttpStatusCode, string)>(responses);
        }

        public int CallCount { get; private set; }
        public List<string> RequestPaths { get; } = [];
        public string? LastPath { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationParameter { get; private set; }
        public string? RefreshRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_responses.TryDequeue(out var next))
            {
                throw new InvalidOperationException("Brak przygotowanej odpowiedzi testowej.");
            }

            CallCount++;
            LastPath = request.RequestUri?.PathAndQuery.TrimStart('/');
            RequestPaths.Add(LastPath ?? string.Empty);
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            if (LastPath == "auth/v1/token?grant_type=refresh_token")
            {
                RefreshRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(next.Json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ConcurrentRefreshHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _bothRefreshesStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refreshCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/auth/v1/token")
            {
                if (Interlocked.Increment(ref _refreshCount) == 2)
                {
                    _bothRefreshesStarted.SetResult();
                }

                await _bothRefreshesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return Json(HttpStatusCode.OK,
                    """{"access_token":"new-access","refresh_token":"new-refresh"}""");
            }

            if (request.RequestUri?.AbsolutePath == "/auth/v1/user")
            {
                return request.Headers.Authorization?.Parameter == "access"
                    ? Json(HttpStatusCode.Unauthorized, "{}")
                    : Json(HttpStatusCode.OK,
                        $"{{\"id\":\"{Guid.NewGuid()}\",\"email\":\"user@example.com\"}}");
            }

            throw new InvalidOperationException("Nieoczekiwane żądanie testowe.");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
            new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
