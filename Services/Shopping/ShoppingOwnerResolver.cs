using E_commerce_backend.Services.Auth;

namespace E_commerce_backend.Services.Shopping;

public sealed record ShoppingOwner(Guid? UserId, string? GuestTokenHash);
public sealed record ShoppingOwnerResult(int Status, ShoppingOwner? Owner = null);

public sealed class ShoppingOwnerResolver(AuthenticatedUserService users)
{
    public async Task<ShoppingOwnerResult> ResolveAsync(HttpRequest request, HttpResponse response,
        bool requireAccount, CancellationToken ct)
    {
        // Even a malformed/expired auth cookie must not silently select the guest cart.
        if (requireAccount || request.Cookies.ContainsKey(AuthCookieService.CookieName))
        {
            var user = await users.GetAsync(request, response, ct);
            return user.Status switch
            {
                AuthenticatedUserStatus.Success when user.UserId is Guid id && id != Guid.Empty => new(200, new(id, null)),
                AuthenticatedUserStatus.Unauthenticated => new(401),
                _ => new(503)
            };
        }
        return new(200, new(null, GuestShoppingCookie.ReadHash(request)));
    }
}
