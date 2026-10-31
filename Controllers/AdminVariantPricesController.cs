using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using System.Linq.Expressions;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Pricing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[Route("api/v1/web/admin/variants/{id:guid}")]
public sealed class AdminVariantPricesController(IAdministratorAccess access, AppDbContext database,
    VariantPriceService prices, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("price")]
    public Task<IActionResult> GetPrice(Guid id, CancellationToken cancellationToken) =>
        AuthorizedAsync(async _ =>
        {
            if (!await database.ProductVariants.AnyAsync(v => v.Id == id, cancellationToken))
                return NotFound(new { message = "Nie znaleziono wariantu." });

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var price = await Rows(id).Where(p => p.EffectiveAt <= now)
                .OrderByDescending(p => p.EffectiveAt).ThenByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id).Select(PriceProjection).FirstOrDefaultAsync(cancellationToken);
            return Ok(new CurrentVariantPriceResponse(price));
        }, cancellationToken);

    [HttpPost("price-changes")]
    public Task<IActionResult> ChangePrice(Guid id, [FromBody] ChangeVariantPriceRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? operationKey,
        [FromHeader(Name = "X-Requested-With")] string? requestedWith,
        CancellationToken cancellationToken) => AuthorizedAsync(async userId =>
        {
            if (requestedWith != "XMLHttpRequest")
                return StatusCode(403, new { code = "csrf_header_required", message = "Żądanie zostało odrzucone." });
            if (request?.BaseAmount is not decimal amount ||
                !Guid.TryParse(operationKey, out var key) || key == Guid.Empty)
                return BadRequest(new { code = "invalid_request", message = "Podaj baseAmount i nagłówek Idempotency-Key z niepustym UUID." });

            var result = await prices.ChangeAsync(new ChangeVariantPrice(id, amount, request.Currency,
                request.Reason, request.ExpectedPriceChangeId, key, userId), cancellationToken);
            if (result.Status is PriceChangeStatus.Applied or PriceChangeStatus.Replayed)
            {
                var price = result.Price!;
                var body = new ChangeVariantPriceResponse(result.Status == PriceChangeStatus.Replayed,
                    new(price.Id, price.VariantId, price.BaseAmount, price.SellingAmount, price.Currency,
                        price.EffectiveAt, price.CreatedAt, price.Reason, price.PreviousPriceChangeId, userId));
                return StatusCode(result.Status == PriceChangeStatus.Applied ? 201 : 200, body);
            }
            return result.Status switch
            {
                PriceChangeStatus.InvalidRequest => BadRequest(new { code = "invalid_request", message = "Podaj poprawną kwotę PLN (0–9999999999999999.99, do 2 miejsc po przecinku), powód (1–120 znaków) i wersję ceny." }),
                PriceChangeStatus.VariantNotFound => NotFound(new { code = "variant_not_found", message = "Nie znaleziono wariantu." }),
                PriceChangeStatus.VersionConflict => Conflict(new { code = "price_version_conflict", message = "Cena została zmieniona. Pobierz jej aktualną wersję." }),
                PriceChangeStatus.OperationKeyConflict => Conflict(new { code = "idempotency_key_conflict", message = "Ten klucz operacji został użyty z innymi danymi." }),
                PriceChangeStatus.DiscountedPriceConflict => Conflict(new { code = "discounted_price_conflict", message = "Zmiana ceny wariantu z rabatem wymaga obsługi promocji." }),
                PriceChangeStatus.FuturePriceConflict => Conflict(new { code = "future_price_conflict", message = "Wariant ma zaplanowaną przyszłą cenę." }),
                _ => Conflict(new { code = "price_timestamp_conflict", message = "Nie można jeszcze nadać późniejszej daty zmiany. Ponów żądanie." })
            };
        }, cancellationToken);

    [HttpGet("price-changes")]
    public Task<IActionResult> GetHistory(Guid id, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken) => AuthorizedAsync(async _ =>
        {
            var selectedPage = page ?? 1;
            var size = pageSize ?? 20;
            var offset = ((long)selectedPage - 1) * size;
            if (selectedPage < 1 || size is < 1 or > 100 || offset > int.MaxValue ||
                (from.HasValue && to.HasValue && from.Value >= to.Value))
                return BadRequest(new { message = "Podaj page >= 1, pageSize od 1 do 100 oraz from < to." });
            if (!await database.ProductVariants.AnyAsync(v => v.Id == id, cancellationToken))
                return NotFound(new { message = "Nie znaleziono wariantu." });

            var rows = Rows(id);
            if (from.HasValue) rows = rows.Where(p => p.EffectiveAt >= from.Value.UtcDateTime);
            if (to.HasValue) rows = rows.Where(p => p.EffectiveAt < to.Value.UtcDateTime);
            var total = await rows.CountAsync(cancellationToken);
            var items = await rows.OrderByDescending(p => p.EffectiveAt).ThenByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id).Skip((int)offset).Take(size).Select(PriceProjection)
                .ToArrayAsync(cancellationToken);
            return Ok(new PriceHistoryResponse(items, selectedPage, size, total,
                (int)Math.Ceiling((double)total / size)));
        }, cancellationToken);

    private IQueryable<VariantPriceChange> Rows(Guid id) => database.VariantPriceChanges.AsNoTracking()
        .Where(p => p.VariantId == id && p.Currency == "PLN");

    private static readonly Expression<Func<VariantPriceChange, AdminPriceChangeResponse>> PriceProjection =
        p => new(p.Id, p.VariantId, p.BaseAmount, p.SellingAmount, p.Currency, p.EffectiveAt,
            p.CreatedAt, p.Reason, p.PreviousPriceChangeId, p.ChangedByUserId);

    private async Task<IActionResult> AuthorizedAsync(Func<Guid, Task<IActionResult>> action,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var authorization = await access.CheckAsync(Request, Response, cancellationToken);
            return authorization.Status switch
            {
                AdministratorAccessStatus.Allowed when authorization.UserId is Guid userId && userId != Guid.Empty
                    => await action(userId),
                AdministratorAccessStatus.Unauthenticated => Unauthorized(new { message = "Brak ważnej sesji." }),
                AdministratorAccessStatus.Forbidden => StatusCode(403, new { message = "Wymagane uprawnienie administratora." }),
                _ => StatusCode(503, new { message = "Obsługa cen jest chwilowo niedostępna." })
            };
        }
        catch (NpgsqlException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (DbUpdateException exception) when (exception.InnerException is NpgsqlException) { return Unavailable(); }
    }

    private ObjectResult Unavailable() => StatusCode(503, new { message = "Obsługa cen jest chwilowo niedostępna." });
}
