using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[Route("api/v1/web/admin/collections")]
[ProducesResponseType<CollectionError>(400)]
[ProducesResponseType<CollectionError>(401)]
[ProducesResponseType<CollectionError>(403)]
[ProducesResponseType<CollectionError>(404)]
[ProducesResponseType<CollectionError>(409)]
[ProducesResponseType<CollectionError>(503)]
public sealed class AdminCollectionsController(IAdministratorAccess access, CollectionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CollectionListResponse>(200)]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken cancellationToken = default) => Run(_ => service.ListAsync(true, page, pageSize, status, cancellationToken), cancellationToken);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CollectionResponse>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => Run(_ => service.GetAsync(true, id, null, cancellationToken), cancellationToken);
    [HttpGet("{id:guid}/products")]
    [ProducesResponseType<CollectionProductsResponse>(200)]
    public Task<IActionResult> Products(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) => Run(_ => service.MembersAsync(id, page, pageSize, cancellationToken), cancellationToken);
    [HttpPost]
    [RequestSizeLimit(1048576)]
    [ProducesResponseType<CollectionCommandResponse>(201)]
    [ProducesResponseType<CollectionCommandResponse>(200)]
    public Task<IActionResult> Create([FromBody] CollectionWriteRequest? body, [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken cancellationToken) => Write("create", Guid.Empty, body, null, key, csrf, cancellationToken);
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(1048576)]
    [ProducesResponseType<CollectionCommandResponse>(200)]
    public Task<IActionResult> Update(Guid id, [FromBody] CollectionWriteRequest? body, [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken cancellationToken) => Write("update", id, body, null, key, csrf, cancellationToken);
    [HttpPut("{id:guid}/products")]
    [RequestSizeLimit(1048576)]
    [ProducesResponseType<CollectionCommandResponse>(200)]
    public Task<IActionResult> SetProducts(Guid id, [FromBody] CollectionProductsRequest? body, [FromHeader(Name = "Idempotency-Key")] string? key,
        [FromHeader(Name = "X-Requested-With")] string? csrf, CancellationToken cancellationToken) => Write("members", id, null, body, key, csrf, cancellationToken);
    private Task<IActionResult> Write(string kind, Guid id, CollectionWriteRequest? body, CollectionProductsRequest? products, string? key, string? csrf, CancellationToken ct) => Run(actor =>
    {
        if (csrf != "XMLHttpRequest") return Task.FromResult(new CollectionResult(403, new CollectionError("csrf_header_required", "Żądanie zostało odrzucone.")));
        if (!Guid.TryParse(key, out var operation) || operation == Guid.Empty) return Task.FromResult(new CollectionResult(400, new CollectionError("invalid_operation_key", "Idempotency-Key musi być UUID.")));
        return service.MutateAsync(kind, id, body, products, operation, actor, ct);
    }, ct);
    private async Task<IActionResult> Run(Func<Guid, Task<CollectionResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var user = await access.CheckAsync(Request, Response, ct);
            if (user.Status == AdministratorAccessStatus.Unauthenticated) return StatusCode(401, new CollectionError("unauthenticated", "Brak ważnej sesji."));
            if (user.Status == AdministratorAccessStatus.Forbidden) return StatusCode(403, new CollectionError("forbidden", "Wymagane uprawnienie administratora."));
            if (user.Status != AdministratorAccessStatus.Allowed || user.UserId is not Guid id || id == Guid.Empty) return Unavailable();
            var result = await action(id); return StatusCode(result.Status, result.Body);
        }
        catch (NpgsqlException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (DbUpdateException e) when (e.InnerException is NpgsqlException) { return Unavailable(); }
    }
    private ObjectResult Unavailable() => StatusCode(503, new CollectionError("unavailable", "Kolekcje są chwilowo niedostępne."));
}
