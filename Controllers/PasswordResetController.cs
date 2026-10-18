using System.Net;
using System.Text.Json;
using E_commerce_backend.Contracts.Auth;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_commerce_backend.Controllers;

[ApiController]
[Route("api/v1/web/auth/password-reset")]
public sealed class PasswordResetController(
    SupabasePasswordResetClient adminClient,
    SupabaseAuthClient authClient,
    IEmailSender emailSender,
    PasswordResetOptions options,
    ILogger<PasswordResetController> logger) : ControllerBase
{
    [HttpPost("request")]
    [EnableRateLimiting("password-reset-request")]
    public async Task<IActionResult> RequestReset([FromBody] RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            using var generated = await adminClient.GenerateLinkAsync(request.Email, cancellationToken);
            if (!generated.IsSuccessStatusCode)
            {
                if (generated.StatusCode != HttpStatusCode.NotFound && generated.StatusCode != HttpStatusCode.BadRequest)
                {
                    logger.LogWarning("Nie udało się wygenerować linku odzyskiwania hasła (status {StatusCode}).", (int)generated.StatusCode);
                }
                return Accepted();
            }

            RecoveryLinkResponse? link;
            try
            {
                link = await generated.Content.ReadFromJsonAsync<RecoveryLinkResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                logger.LogWarning("Supabase zwrócił nieprawidłową odpowiedź dla linku odzyskiwania hasła.");
                return Accepted();
            }

            if (string.IsNullOrWhiteSpace(link?.HashedToken))
            {
                logger.LogWarning("Supabase nie zwrócił tokenu odzyskiwania hasła.");
                return Accepted();
            }

            var url = new UriBuilder(options.Url)
            {
                Fragment = "token_hash=" + Uri.EscapeDataString(link.HashedToken)
            }.Uri.AbsoluteUri;
            var sent = await emailSender.SendAsync(new EmailMessage(
                options.FromEmail,
                options.FromName,
                request.Email,
                "Zresetuj hasło",
                "Password Reset",
                $"Aby ustawić nowe hasło, otwórz ten link: {url}\n\nJeśli nie prosiłeś o zmianę hasła, zignoruj tę wiadomość."),
                cancellationToken);
            if (!sent)
            {
                logger.LogWarning("Mailtrap nie przyjął wiadomości odzyskiwania hasła.");
            }
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Usługa odzyskiwania hasła jest niedostępna.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Przekroczono czas żądania odzyskiwania hasła.");
        }

        return Accepted();
    }

    [HttpPost("confirm")]
    [EnableRateLimiting("password-reset-confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmPasswordResetRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            using var verified = await authClient.VerifyRecoveryAsync(request.TokenHash, cancellationToken);
            if (verified.StatusCode == HttpStatusCode.TooManyRequests)
                return StatusCode(429, new { message = "Spróbuj ponownie później." });
            if ((int)verified.StatusCode >= 500)
                return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });
            if (!verified.IsSuccessStatusCode)
                return BadRequest(new { message = "Link jest nieprawidłowy lub wygasł." });

            RecoverySessionResponse? session;
            try
            {
                session = await verified.Content.ReadFromJsonAsync<RecoverySessionResponse>(cancellationToken);
            }
            catch (JsonException)
            {
                return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });
            }
            if (string.IsNullOrWhiteSpace(session?.AccessToken))
                return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });

            using var updated = await authClient.UpdatePasswordAsync(session.AccessToken, request.NewPassword, cancellationToken);
            if (updated.IsSuccessStatusCode)
                return NoContent();
            if (updated.StatusCode == HttpStatusCode.TooManyRequests)
                return StatusCode(429, new { message = "Spróbuj ponownie później." });
            if ((int)updated.StatusCode >= 500)
                return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });

            SupabaseAuthError? error = null;
            try
            {
                error = await updated.Content.ReadFromJsonAsync<SupabaseAuthError>(cancellationToken);
            }
            catch (JsonException) { }
            return error?.ErrorCode == "weak_password"
                ? BadRequest(new { message = "Hasło nie spełnia wymagań." })
                : StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(503, new { message = "Reset hasła jest chwilowo niedostępny." });
        }
    }
}
