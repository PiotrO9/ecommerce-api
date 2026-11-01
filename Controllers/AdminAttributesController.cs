using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/admin/attribute-keys")]
public sealed class AdminAttributesController(IAdministratorAccess access, CatalogManagementReader reader, AttributeManagementService service)
    : AdminCatalogControllerBase(access, reader)
{
    [HttpGet]
    [ProducesResponseType<AdminPage<AdminAttributeKey>>(200)]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Read(() => Reader.KeysAsync(page, pageSize, ct), ct);
    [HttpPost]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Create([FromBody] KeyCreateRequest body, CancellationToken ct) => Write((actor, key) => service.CreateKeyAsync(body, actor, key, ct), ct);
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Edit(Guid id, [FromBody] KeyEditRequest body, CancellationToken ct) => Write((actor, key) => service.EditKeyAsync(id, body, actor, key, ct), ct);
    [HttpGet("{id:guid}/values")]
    [ProducesResponseType<AdminPage<AdminAttributePair>>(200)]
    public Task<IActionResult> Values(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Read(() => Reader.PairsAsync(id, page, pageSize, ct), ct);
    [HttpPost("{id:guid}/values")]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> CreateValue(Guid id, [FromBody] PairCreateRequest body, CancellationToken ct) => Write((actor, key) => service.CreatePairAsync(id, body, actor, key, ct), ct);
}
