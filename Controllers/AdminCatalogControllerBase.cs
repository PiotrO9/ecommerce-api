using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Services.Auth;
using E_commerce_backend.Services.Catalog.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[CatalogNoStore]
[RequestSizeLimit(1048576)]
[ProducesResponseType<CatalogError>(400)]
[ProducesResponseType<CatalogError>(401)]
[ProducesResponseType<CatalogError>(403)]
[ProducesResponseType<CatalogError>(404)]
[ProducesResponseType<CatalogError>(409)]
[ProducesResponseType<CatalogError>(503)]
public abstract class AdminCatalogControllerBase(IAdministratorAccess access, CatalogManagementReader reader) : ControllerBase
{
    protected CatalogManagementReader Reader { get; } = reader;
    protected Task<IActionResult> Read(Func<Task<CatalogResult>> action, CancellationToken ct) =>
        Run(_ => Reader.SnapshotAsync(action, ct), ct);
    protected Task<IActionResult> Write(Func<Guid, Guid, Task<CatalogResult>> action, CancellationToken ct) => Run(actor =>
    {
        if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            throw new CatalogValidation("csrf_header_required", status: 403);
        if (!Guid.TryParse(Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
            throw new CatalogValidation("invalid_operation_key", "Idempotency-Key");
        return action(actor, key);
    }, ct);
    private async Task<IActionResult> Run(Func<Guid, Task<CatalogResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var user = await access.CheckAsync(Request, Response, ct);
            if (user.Status == AdministratorAccessStatus.Unauthenticated) return StatusCode(401, new CatalogError("unauthenticated", "Brak ważnej sesji."));
            if (user.Status == AdministratorAccessStatus.Forbidden) return StatusCode(403, new CatalogError("forbidden", "Wymagane uprawnienie administratora."));
            if (user.Status != AdministratorAccessStatus.Allowed || user.UserId is not Guid id || id == Guid.Empty) return Unavailable();
            var result = await action(id);
            return result.Body is null ? StatusCode(result.Status) : StatusCode(result.Status, result.Body);
        }
        catch (CatalogValidation e) { return StatusCode(e.Status, new CatalogError(e.Code, Message(e.Code), e.Field)); }
        catch (NpgsqlException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (DbUpdateException e) when (e.InnerException is NpgsqlException) { return Unavailable(); }
    }
    private static string Message(string code) => code switch
    {
        "version_conflict" => "Dane zostały zmienione. Odczytaj aktualną wersję.",
        "idempotency_key_conflict" => "Klucz operacji został już użyty z innymi danymi.",
        "variant_configuration_locked" => "Utwórz nowy wariant, aby zmienić konfigurację oferty z historią.",
        "duplicate_variant_configuration" => "Produkt ma już wariant z takim zestawem cech.",
        "missing_current_pln_price" => "Przed publikacją ustaw obowiązującą cenę PLN.",
        "missing_category" => "Przed publikacją przypisz kategorię.",
        "no_published_variant" => "Opublikowany produkt wymaga co najmniej jednego opublikowanego wariantu.",
        _ => "Sprawdź dane żądania i aktualny stan zasobu."
    };
    private ObjectResult Unavailable() => StatusCode(503, new CatalogError("catalog_unavailable", "Zarządzanie ofertą jest chwilowo niedostępne."));
}

public sealed class CatalogNoStoreAttribute : ResultFilterAttribute, IAlwaysRunResultFilter
{
    public override void OnResultExecuting(ResultExecutingContext context) => context.HttpContext.Response.Headers.CacheControl = "no-store";
}
