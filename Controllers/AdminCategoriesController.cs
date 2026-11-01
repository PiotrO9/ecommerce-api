using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/admin/categories")]
public sealed class AdminCategoriesController(IAdministratorAccess access, CatalogManagementReader reader, CategoryManagementService service)
    : AdminCatalogControllerBase(access, reader)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminCategoryNode>>(200)]
    public Task<IActionResult> List(CancellationToken ct) => Read(() => Reader.CategoriesAsync(null, ct), ct);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminCategory>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Read(() => Reader.CategoriesAsync(id, ct), ct);
    [HttpPost]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Create([FromBody] CategoryCreateRequest body, CancellationToken ct) => Write((actor, key) => service.CreateAsync(body, actor, key, ct), ct);
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Edit(Guid id, [FromBody] CategoryEditRequest body, CancellationToken ct) => Write((actor, key) => service.EditAsync(id, body, actor, key, ct), ct);
    [HttpPut("{id:guid}/parent")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Parent(Guid id, [FromBody] CategoryParentRequest body, CancellationToken ct) => Write((actor, key) => service.ParentAsync(id, body, actor, key, ct), ct);
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Delete(Guid id, [FromQuery] long expectedVersion, CancellationToken ct) => Write((actor, key) => service.DeleteAsync(id, expectedVersion, actor, key, ct), ct);
}
