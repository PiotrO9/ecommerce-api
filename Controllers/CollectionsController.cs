using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[Route("api/v1/web/collections")]
public sealed class CollectionsController(CollectionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CollectionListResponse>(200)]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Run(() => service.ListAsync(false, page, pageSize, null, cancellationToken));
    [HttpGet("details")]
    [ProducesResponseType<CollectionResponse>(200)]
    public Task<IActionResult> Get([FromQuery] Guid? id, [FromQuery] string? slug, CancellationToken cancellationToken) =>
        Run(() => service.GetAsync(false, id, slug, cancellationToken));
    private async Task<IActionResult> Run(Func<Task<CollectionResult>> action)
    {
        try { var r = await action(); return StatusCode(r.Status, r.Body); }
        catch (NpgsqlException) { return StatusCode(503, new CollectionError("unavailable", "Kolekcje są chwilowo niedostępne.")); }
        catch (TimeoutException) { return StatusCode(503, new CollectionError("unavailable", "Kolekcje są chwilowo niedostępne.")); }
    }
}
