using System.Net;
using System.Text;
using E_commerce_backend.Contracts.Auth;
using E_commerce_backend.Controllers;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace E_commerce_backend.Tests.Unit;

public sealed class PasswordResetTests
{
    [Fact]
    public async Task RequestReset_SendsLinkAndReturnsAccepted()
    {
        var sender = new StubEmailSender();
        using var admin = Client((request) =>
        {
            Assert.Equal("/auth/v1/admin/generate_link", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.OK, "{\"hashed_token\":\"abc+123\"}");
        });
        using var auth = Client(_ => throw new InvalidOperationException());
        var controller = Controller(admin, auth, sender);

        var result = await controller.RequestReset(new RequestPasswordResetRequest { Email = "user@example.com" }, default);

        Assert.IsType<AcceptedResult>(result);
        Assert.Contains("https://shop.example/reset#token_hash=abc%2B123", sender.Text);
        Assert.Equal("user@example.com", sender.To);
    }

    [Fact]
    public async Task RequestReset_DoesNotRevealMissingAccount()
    {
        var sender = new StubEmailSender();
        using var admin = Client(_ => Json(HttpStatusCode.NotFound, "{}"));
        using var auth = Client(_ => throw new InvalidOperationException());

        var result = await Controller(admin, auth, sender).RequestReset(
            new RequestPasswordResetRequest { Email = "missing@example.com" }, default);

        Assert.IsType<AcceptedResult>(result);
        Assert.Null(sender.To);
    }

    [Fact]
    public async Task Confirm_VerifiesRecoveryTokenBeforeUpdatingPassword()
    {
        var calls = 0;
        using var admin = Client(_ => throw new InvalidOperationException());
        using var auth = Client(request =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal("/auth/v1/verify", request.RequestUri!.AbsolutePath);
                return Json(HttpStatusCode.OK, "{\"access_token\":\"recovery-access\"}");
            }
            Assert.Equal("/auth/v1/user", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer recovery-access", request.Headers.Authorization!.ToString());
            return Json(HttpStatusCode.OK, "{}");
        });

        var result = await Controller(admin, auth, new StubEmailSender()).Confirm(
            new ConfirmPasswordResetRequest { TokenHash = "token", NewPassword = "NewSecret1!", ConfirmPassword = "NewSecret1!" }, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Confirm_RejectsInvalidTokenWithoutUpdatingPassword()
    {
        var calls = 0;
        using var admin = Client(_ => throw new InvalidOperationException());
        using var auth = Client(_ =>
        {
            calls++;
            return Json(HttpStatusCode.BadRequest, "{}");
        });

        var result = await Controller(admin, auth, new StubEmailSender()).Confirm(
            new ConfirmPasswordResetRequest { TokenHash = "invalid", NewPassword = "NewSecret1!", ConfirmPassword = "NewSecret1!" }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MailtrapSender_SendsTransactionalPayload()
    {
        using var client = Client(request =>
        {
            Assert.Equal("https://example.test/api/send", request.RequestUri!.AbsoluteUri);
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"subject\":\"Reset\"", body);
            Assert.Contains("\"email\":\"user@example.com\"", body);
            Assert.Contains("\"name\":\"E-commerce\"", body);
            Assert.Contains("\"category\":\"Integration Test\"", body);
            return Json(HttpStatusCode.OK, "{\"success\":true}");
        });

        var sent = await new MailtrapEmailSender(client).SendAsync(
            new EmailMessage("sender@example.com", "E-commerce", "user@example.com", "Reset", "Integration Test", "Link"), default);

        Assert.True(sent);
    }

    private static PasswordResetController Controller(HttpClient admin, HttpClient auth, StubEmailSender sender) =>
        new(new SupabasePasswordResetClient(admin), new SupabaseAuthClient(auth), sender,
            new PasswordResetOptions(new Uri("https://shop.example/reset"), "sender@example.com", "E-commerce"),
            NullLogger<PasswordResetController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new(new StubHandler(response)) { BaseAddress = new Uri("https://example.test/") };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class StubEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public string Text { get; private set; } = string.Empty;

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            To = message.ToEmail;
            Text = message.Text;
            return Task.FromResult(true);
        }
    }
}
