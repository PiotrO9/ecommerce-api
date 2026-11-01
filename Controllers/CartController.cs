using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/cart")]
public sealed class CartController(ShoppingOwnerResolver resolver, CartService service) : ShoppingControllerBase(resolver)
{
    [HttpGet]
    [ProducesResponseType<CartResponse>(200)]
    public Task<IActionResult> Get(CancellationToken ct) => Run(false, o => service.GetAsync(o, ct), ct);

    [HttpPut("items/{variantId:guid}")]
    [EnableRateLimiting("shopping-write")]
    [RequestSizeLimit(4096)]
    [Consumes("application/json")]
    [ProducesResponseType<ShoppingVersionResponse>(200)]
    public Task<IActionResult> Put(Guid variantId, [FromBody] CartItemRequest body,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(false, o => body.Quantity is null
            ? Task.FromResult(ShoppingResult.Error(400, "quantity_required", "Podaj ilość."))
            : service.MutateAsync(o, variantId, body.Quantity, body, false, ct), ct, true, csrf);

    [HttpDelete("items/{variantId:guid}")]
    [EnableRateLimiting("shopping-write")]
    [ProducesResponseType<ShoppingVersionResponse>(200)]
    public Task<IActionResult> Delete(Guid variantId, [FromQuery] ShoppingVersionRequest stamp,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(false, o => service.MutateAsync(o, variantId, null, stamp, false, ct), ct, true, csrf);

    [HttpDelete]
    [EnableRateLimiting("shopping-write")]
    [ProducesResponseType<ShoppingVersionResponse>(200)]
    public Task<IActionResult> Clear([FromQuery] ShoppingVersionRequest stamp,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(false, o => service.MutateAsync(o, null, null, stamp, true, ct), ct, true, csrf);
}
