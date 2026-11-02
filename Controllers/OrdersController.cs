using E_commerce_backend.Contracts.Inventory;
using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Inventory;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/orders")]
public sealed class OrdersController(ShoppingOwnerResolver resolver, InventoryOrderService service) : ShoppingControllerBase(resolver)
{
    [HttpPost]
    [EnableRateLimiting("shopping-write")]
    [Consumes("application/json")]
    [RequestSizeLimit(32768)]
    [ProducesResponseType<OrderResponse>(201)]
    public Task<IActionResult> Create([FromBody] OrderCreateRequest body,
        [FromHeader(Name = "X-Requested-With")] string? csrf,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(true, o => Guid.TryParse(key, out var id) && id != Guid.Empty
            ? service.CreateAsync(o.UserId!.Value, id, body, ct)
            : Task.FromResult(ShoppingResult.Error(400, "invalid_operation_key", "Podaj UUID w Idempotency-Key.")), ct, true, csrf);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(true, o => service.GetAsync(o.UserId!.Value, id, ct), ct);

    [HttpGet]
    [ProducesResponseType<OrderPage>(200)]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Run(true, o => service.ListAsync(o.UserId!.Value, page, pageSize, ct), ct);

    [HttpPost("{id:guid}/cancel")]
    [EnableRateLimiting("shopping-write")]
    [ProducesResponseType<OrderResponse>(200)]
    public Task<IActionResult> Cancel(Guid id, [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken ct) =>
        Run(true, o => service.CancelAsync(o.UserId!.Value, id, ct), ct, true, csrf);
}
