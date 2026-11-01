using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace E_commerce_backend.Services.Shopping;

public static class GuestShoppingCookie
{
    public const string Name = "__Host-ecomm-shopping";

    public static (string Token, string Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return (WebEncoders.Base64UrlEncode(bytes), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public static string? ReadHash(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out var token) || token.Length != 43) return null;
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(token);
            return bytes.Length == 32 ? Convert.ToHexString(SHA256.HashData(bytes)) : null;
        }
        catch (FormatException) { return null; }
    }

    public static void Append(HttpResponse response, string token, DateTime expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTime? expires) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/",
        Expires = expires is DateTime date ? new DateTimeOffset(date) : null,
        IsEssential = true
    };
}
