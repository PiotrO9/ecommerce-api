using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers;

[Route("api/v1/web/admin/products")]
public sealed class AdminProductsController(IAdministratorAccess access, CatalogManagementReader reader, ProductManagementService service)
    : AdminCatalogControllerBase(access, reader)
{
    [HttpPost("search")]
    [ProducesResponseType<AdminPage<AdminProductRow>>(200)]
    public Task<IActionResult> Search([FromBody] AdminProductSearchRequest body, CancellationToken ct) => Read(() => Reader.ProductsAsync(body, ct), ct);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminProductDetails>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Read(() => Reader.ProductAsync(id, ct), ct);
    [HttpGet("{id:guid}/variants")]
    [ProducesResponseType<AdminPage<AdminVariantSummary>>(200)]
    public Task<IActionResult> Variants(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Read(() => Reader.VariantsAsync(id, page, pageSize, ct), ct);
    [HttpPost]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Create([FromBody] ProductCreateRequest body, CancellationToken ct) => Write((actor, key) => service.CreateAsync(body, actor, key, ct), ct);
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Edit(Guid id, [FromBody] ProductEditRequest body, CancellationToken ct) => Write((actor, key) => service.EditAsync(id, body, actor, key, ct), ct);
    [HttpPut("{id:guid}/categories")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Categories(Guid id, [FromBody] CategoryIdsRequest body, CancellationToken ct) => Write((actor, key) => service.CategoriesAsync(id, body, actor, key, ct), ct);
    [HttpPost("{id:guid}/variants")]
    [ProducesResponseType<CatalogCommandResponse>(201)]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> AddVariant(Guid id, [FromBody] VariantCreateRequest body, CancellationToken ct) => Write((actor, key) => service.AddVariantAsync(id, body, actor, key, ct), ct);
    [HttpGet("{id:guid}/publication-readiness")]
    [ProducesResponseType<PublicationReadiness>(200)]
    public Task<IActionResult> Readiness(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Read(() => Reader.ReadinessAsync(id, false, page, pageSize, ct), ct);
    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<CatalogCommandResponse>(200)]
    public Task<IActionResult> Status(Guid id, [FromBody] StatusRequest body, CancellationToken ct) => Write((actor, key) => service.StatusAsync(id, body, actor, key, ct), ct);
}
