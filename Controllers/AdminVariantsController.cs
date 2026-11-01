using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/admin/variants")]
public sealed class AdminVariantsController(IAdministratorAccess access, CatalogManagementReader reader, ProductManagementService service)
    : AdminCatalogControllerBase(access, reader)
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminVariantDetails>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Read(() => Reader.VariantAsync(id, ct), ct);
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Edit(Guid id, [FromBody] VariantEditRequest body, CancellationToken ct) => Write((actor, key) => service.EditVariantAsync(id, body, actor, key, ct), ct);
    [HttpPut("{id:guid}/attributes")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Attributes(Guid id, [FromBody] AttributeIdsRequest body, CancellationToken ct) => Write((actor, key) => service.AttributesAsync(id, body, actor, key, ct), ct);
    [HttpGet("{id:guid}/publication-readiness")]
    [ProducesResponseType<VariantReadiness>(200)]
    public Task<IActionResult> Readiness(Guid id, CancellationToken ct) => Read(() => Reader.ReadinessAsync(id, true, 1, 1, ct), ct);
    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Status(Guid id, [FromBody] StatusRequest body, CancellationToken ct) => Write((actor, key) => service.VariantStatusAsync(id, body, actor, key, ct), ct);
}
