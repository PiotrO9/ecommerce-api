using System.Net;
using System.Text.Json;
using E_commerce_backend.Contracts.Auth;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Profile;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace E_commerce_backend.Controllers
{
    public class AuthController : WebApiControllerBase
    {
        private const string LoginUnavailableMessage = "Logowanie jest chwilowo niedostępne.";
        private const string RegisterUnavailableMessage = "Rejestracja jest chwilowo niedostępna.";
        private const string RateLimitedMessage = "Spróbuj ponownie później.";
        private const string SessionMissingMessage = "Brak ważnej sesji.";
        private const string MeUnavailableMessage = "Dane użytkownika są chwilowo niedostępne.";
        private const string LogoutUnavailableMessage = "Wylogowanie jest chwilowo niedostępne.";

        private readonly SupabaseAuthClient _supabaseAuthClient;
        private readonly AuthCookieService _authCookieService;
        private readonly AuthenticatedUserService _authenticatedUserService;
        private readonly IUserProfileReader _userProfileReader;

        public AuthController(
            SupabaseAuthClient supabaseAuthClient,
            AuthCookieService authCookieService,
            IUserProfileReader userProfileReader)
        {
            _supabaseAuthClient = supabaseAuthClient;
            _authCookieService = authCookieService;
            _authenticatedUserService = new AuthenticatedUserService(supabaseAuthClient, authCookieService);
            _userProfileReader = userProfileReader;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            try
            {
                using var response = await _supabaseAuthClient.SignInAsync(
                    request.Email,
                    request.Password,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return StatusCode(429, new { message = RateLimitedMessage });
                }

                if ((int)response.StatusCode >= 500)
                {
                    return StatusCode(503, new { message = LoginUnavailableMessage });
                }

                if (!response.IsSuccessStatusCode)
                {
                    string? errorCode = null;

                    try
                    {
                        var error = await response.Content.ReadFromJsonAsync<SupabaseAuthError>(
                            cancellationToken);
                        errorCode = error?.ErrorCode;
                    }
                    catch (JsonException)
                    {
                        // Nie przekazujemy nieznanej odpowiedzi Supabase do frontendu.
                    }

                    return errorCode switch
                    {
                        "invalid_credentials" => Unauthorized(new
                        {
                            message = "Nieprawidłowy e-mail lub hasło."
                        }),
                        _ => StatusCode(503, new { message = LoginUnavailableMessage })
                    };
                }

                SupabaseSignInResponse? signIn;
                try
                {
                    signIn = await response.Content.ReadFromJsonAsync<SupabaseSignInResponse>(
                        cancellationToken);
                }
                catch (JsonException)
                {
                    return StatusCode(503, new { message = LoginUnavailableMessage });
                }

                if (string.IsNullOrWhiteSpace(signIn?.AccessToken) ||
                    string.IsNullOrWhiteSpace(signIn.RefreshToken))
                {
                    return StatusCode(503, new { message = LoginUnavailableMessage });
                }

                var absoluteExpiresAt = DateTimeOffset.UtcNow.AddDays(request.RememberMe ? 30 : 1);
                var session = new AuthSessionPayload(
                    signIn.AccessToken,
                    signIn.RefreshToken,
                    absoluteExpiresAt);

                if (!_authCookieService.TryAppend(Response, session))
                {
                    return StatusCode(503, new { message = LoginUnavailableMessage });
                }

                return Ok();
            }
            catch (HttpRequestException)
            {
                return StatusCode(503, new { message = LoginUnavailableMessage });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(503, new { message = LoginUnavailableMessage });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(
            [FromBody] RegisterRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _supabaseAuthClient.SignUpAsync(
                    request.Email,
                    request.Password,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return StatusCode(StatusCodes.Status201Created);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return StatusCode(429, new { message = RateLimitedMessage });
                }

                if ((int)response.StatusCode >= 500)
                {
                    return StatusCode(503, new { message = RegisterUnavailableMessage });
                }

                string? errorCode = null;

                try
                {
                    var error = await response.Content.ReadFromJsonAsync<SupabaseAuthError>(
                        cancellationToken);
                    errorCode = error?.ErrorCode;
                }
                catch (JsonException)
                {
                    // Nie przekazujemy nieznanej odpowiedzi Supabase do frontendu.
                }

                return errorCode switch
                {
                    "weak_password" => BadRequest(new
                    {
                        message = "Hasło nie spełnia wymagań."
                    }),
                    "validation_failed" or "email_address_invalid" => BadRequest(new
                    {
                        message = "Nieprawidłowe dane rejestracji."
                    }),
                    "user_already_exists" or "email_exists" => Conflict(new
                    {
                        message = "Nie można utworzyć konta dla tego adresu e-mail."
                    }),
                    _ => StatusCode(503, new
                    {
                        message = RegisterUnavailableMessage
                    })
                };
            }
            catch (HttpRequestException)
            {
                return StatusCode(503, new { message = RegisterUnavailableMessage });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(503, new { message = RegisterUnavailableMessage });
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(
            [FromHeader(Name = "X-Requested-With")] string? requestedWith,
            CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            // Własny nagłówek wymusza preflight przy żądaniu z obcej strony.
            if (requestedWith != "XMLHttpRequest")
            {
                return StatusCode(403, new { message = "Żądanie zostało odrzucone." });
            }

            var session = _authCookieService.Read(Request);
            if (session is null)
            {
                _authCookieService.Delete(Response);
                return NoContent();
            }

            try
            {
                using var response = await _supabaseAuthClient.SignOutAsync(
                    session.AccessToken, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var refresh = await _authenticatedUserService.RefreshAsync(
                        session, Response, cancellationToken);
                    if (refresh.Status == SessionRefreshStatus.Invalid)
                    {
                        return NoContent();
                    }

                    if (refresh.Status == SessionRefreshStatus.Unavailable)
                    {
                        return StatusCode(503, new { message = LogoutUnavailableMessage });
                    }

                    using var retry = await _supabaseAuthClient.SignOutAsync(
                        refresh.Session!.AccessToken, cancellationToken);
                    if (retry.IsSuccessStatusCode ||
                        retry.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        _authCookieService.Delete(Response);
                        return NoContent();
                    }

                    return StatusCode(503, new { message = LogoutUnavailableMessage });
                }

                if (response.IsSuccessStatusCode)
                {
                    _authCookieService.Delete(Response);
                    return NoContent();
                }

                return StatusCode(503, new { message = LogoutUnavailableMessage });
            }
            catch (HttpRequestException)
            {
                return StatusCode(503, new { message = LogoutUnavailableMessage });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(503, new { message = LogoutUnavailableMessage });
            }
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            Response.Headers.CacheControl = "no-store";
            var user = await _authenticatedUserService.GetAsync(Request, Response, cancellationToken);
            if (user.Status == AuthenticatedUserStatus.Unauthenticated)
            {
                return Unauthorized(new { message = SessionMissingMessage });
            }

            if (user.Status == AuthenticatedUserStatus.Unavailable)
            {
                return StatusCode(503, new { message = MeUnavailableMessage });
            }

            try
            {
                var profile = await _userProfileReader.GetAsync(user.UserId!.Value, cancellationToken);
                return Ok(new
                {
                    id = user.UserId,
                    email = user.Email,
                    firstName = profile?.FirstName,
                    lastName = profile?.LastName
                });
            }
            catch (NpgsqlException)
            {
                return StatusCode(503, new { message = MeUnavailableMessage });
            }
            catch (TimeoutException)
            {
                return StatusCode(503, new { message = MeUnavailableMessage });
            }
        }

    }
}
