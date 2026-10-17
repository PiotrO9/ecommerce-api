using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace E_commerce_backend.Services.Auth;

public sealed class AuthCookieService
{
    public const string CookieName = "__Host-ecomm-auth";
    private const string ProtectionPurpose = "ECommerce.AuthSession.v1";
    private const int MaxCookieBytes = 3800;

    private readonly IDataProtector _protector;

    public AuthCookieService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
    }

    public bool TryAppend(HttpResponse response, AuthSessionPayload session)
    {
        var protectedValue = _protector.Protect(JsonSerializer.Serialize(session));

        // Przeglądarki zwykle ograniczają pojedyncze ciasteczko do około 4 KiB.
        if (Encoding.UTF8.GetByteCount($"{CookieName}={protectedValue}") > MaxCookieBytes)
        {
            return false;
        }

        response.Cookies.Append(CookieName, protectedValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = session.AbsoluteExpiresAt,
            IsEssential = true
        });

        return true;
    }

    public AuthSessionPayload? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var protectedValue) ||
            string.IsNullOrWhiteSpace(protectedValue) ||
            protectedValue.Length > MaxCookieBytes)
        {
            return null;
        }

        try
        {
            var session = JsonSerializer.Deserialize<AuthSessionPayload>(
                _protector.Unprotect(protectedValue));

            return session is not null &&
                !string.IsNullOrWhiteSpace(session.AccessToken) &&
                !string.IsNullOrWhiteSpace(session.RefreshToken) &&
                session.AbsoluteExpiresAt > DateTimeOffset.UtcNow
                ? session : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Delete(HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }
}
