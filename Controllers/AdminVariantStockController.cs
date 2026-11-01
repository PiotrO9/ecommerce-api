using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/admin/variants/{id:guid}/stock")]
public sealed class AdminVariantStockController(IAdministratorAccess access, CatalogManagementReader reader, VariantStockService service)
    : AdminCatalogControllerBase(access, reader)
{
    [HttpGet]
    [ProducesResponseType<AdminStockResponse>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Read(() => Reader.StockAsync(id, ct), ct);
    [HttpPut]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Set(Guid id, [FromBody] StockSetRequest body, CancellationToken ct) => Write((actor, key) => service.SetAsync(id, body, actor, key, ct), ct);
}
