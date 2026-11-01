using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[Route("api/v1/web/admin/promotions")]
[ProducesResponseType<PromotionError>(400)]
[ProducesResponseType<PromotionError>(401)]
[ProducesResponseType<PromotionError>(403)]
[ProducesResponseType<PromotionError>(404)]
[ProducesResponseType<PromotionError>(409)]
[ProducesResponseType<PromotionError>(503)]
public sealed class AdminPromotionsController(IAdministratorAccess access, PromotionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PromotionListResponse>(200)]
    public Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] Guid? productId = null, CancellationToken cancellationToken = default,
        [FromQuery] Guid? collectionId = null, [FromQuery] string? scope = null) =>
        Run(_ => service.ListAsync(page, pageSize, status, productId, cancellationToken, collectionId, scope), cancellationToken);
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PromotionResponse>(200)]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => Run(_ => service.GetAsync(id, cancellationToken), cancellationToken);
    [HttpPost]
    [ProducesResponseType<PromotionCommandResponse>(201)]
    [ProducesResponseType<PromotionCommandResponse>(200)]
    public Task<IActionResult> Create([FromBody] PromotionDraftRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? key, [FromHeader(Name = "X-Requested-With")] string? csrf,
        CancellationToken cancellationToken) => Mutate("create", Guid.Empty, request, null, key, csrf, cancellationToken);
    [HttpPut("{id:guid}")]
    [ProducesResponseType<PromotionCommandResponse>(200)]
    public Task<IActionResult> Update(Guid id, [FromBody] PromotionDraftRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? key, [FromHeader(Name = "X-Requested-With")] string? csrf,
        CancellationToken cancellationToken) => Mutate("update", id, request, null, key, csrf, cancellationToken);
    [HttpPost("{id:guid}/preview")]
    [ProducesResponseType<PromotionPreviewResponse>(200)]
    public Task<IActionResult> Preview(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) => Run(_ => service.PreviewAsync(id, page, pageSize, cancellationToken), cancellationToken);
    [HttpPost("{id:guid}/activate")]
    [RequestSizeLimit(1048576)]
    [ProducesResponseType<PromotionCommandResponse>(200)]
    public Task<IActionResult> Activate(Guid id, [FromBody] PromotionActivationRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? key, [FromHeader(Name = "X-Requested-With")] string? csrf,
        CancellationToken cancellationToken) => Mutate("activate", id, null, request, key, csrf, cancellationToken);
    [HttpPost("{id:guid}/end")]
    [ProducesResponseType<PromotionCommandResponse>(200)]
    public Task<IActionResult> End(Guid id, [FromBody] PromotionVersionRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? key, [FromHeader(Name = "X-Requested-With")] string? csrf,
        CancellationToken cancellationToken) => Mutate("end", id, null, request, key, csrf, cancellationToken);
    private Task<IActionResult> Mutate(string kind, Guid id, PromotionDraftRequest? draft, PromotionVersionRequest? version,
        string? key, string? csrf, CancellationToken ct) => Run(user =>
        {
            if (csrf != "XMLHttpRequest") return Task.FromResult(new PromotionResult(403, new PromotionError("csrf_header_required", "Żądanie zostało odrzucone.")));
            if (!Guid.TryParse(key, out var operationKey) || operationKey == Guid.Empty)
                return Task.FromResult(new PromotionResult(400, new PromotionError("invalid_operation_key", "Idempotency-Key musi być UUID.")));
            return service.MutateAsync(kind, id, draft, version, operationKey, user, ct);
        }, ct);
    private async Task<IActionResult> Run(Func<Guid, Task<PromotionResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var user = await access.CheckAsync(Request, Response, ct);
            if (user.Status == AdministratorAccessStatus.Unauthenticated) return Unauthorized(new PromotionError("unauthenticated", "Brak ważnej sesji."));
            if (user.Status == AdministratorAccessStatus.Forbidden) return StatusCode(403, new PromotionError("forbidden", "Wymagane uprawnienie administratora."));
            if (user.Status != AdministratorAccessStatus.Allowed || user.UserId is not Guid id || id == Guid.Empty) return Unavailable();
            var result = await action(id);
            return StatusCode(result.Status, result.Body);
        }
        catch (NpgsqlException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (DbUpdateException e) when (e.InnerException is NpgsqlException) { return Unavailable(); }
    }
    private ObjectResult Unavailable() => StatusCode(503, new PromotionError("unavailable", "Obsługa promocji jest chwilowo niedostępna."));
}
