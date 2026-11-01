using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/favorites")]
public sealed class FavoritesController(ShoppingOwnerResolver resolver, FavoritesService service) : ShoppingControllerBase(resolver)
{
    [HttpGet]
    [ProducesResponseType<FavoritesResponse>(200)]
    public Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Run(true, o => service.GetAsync(o.UserId!.Value, page, pageSize, ct), ct);

    [HttpPut("items/{variantId:guid}")]
    [EnableRateLimiting("shopping-write")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<ShoppingVersionResponse>(200)]
    public Task<IActionResult> Put(Guid variantId, [FromBody] ShoppingVersionRequest stamp,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(true, o => service.MutateAsync(o.UserId!.Value, variantId, stamp, true, ct), ct, true, csrf);

    [HttpDelete("items/{variantId:guid}")]
    [EnableRateLimiting("shopping-write")]
    [ProducesResponseType<ShoppingVersionResponse>(200)]
    public Task<IActionResult> Delete(Guid variantId, [FromQuery] ShoppingVersionRequest stamp,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(true, o => service.MutateAsync(o.UserId!.Value, variantId, stamp, false, ct), ct, true, csrf);
}
