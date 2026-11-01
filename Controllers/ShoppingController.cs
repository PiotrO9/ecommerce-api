using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/shopping")]
public sealed class ShoppingController(ShoppingOwnerResolver resolver, GuestShoppingSessionService service) : ShoppingControllerBase(resolver)
{
    [HttpPost("guest-session")]
    [EnableRateLimiting("shopping-guest-create")]
    [ProducesResponseType<GuestShoppingSessionResponse>(200)]
    [ProducesResponseType<GuestShoppingSessionResponse>(201)]
    public Task<IActionResult> Create([FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(false, o => service.CreateAsync(o, Response, ct), ct, true, csrf);

    [HttpDelete("guest-session")]
    [EnableRateLimiting("shopping-write")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete([FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(false, _ => service.DeleteAsync(GuestShoppingCookie.ReadHash(Request), Response, ct), ct, true, csrf);
}
