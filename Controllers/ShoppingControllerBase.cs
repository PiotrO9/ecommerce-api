using E_commerce_backend.Contracts.Shopping;
using E_commerce_backend.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Controllers;

[ApiController]
[ProducesResponseType<ShoppingError>(400)]
[ProducesResponseType<ShoppingError>(401)]
[ProducesResponseType<ShoppingError>(403)]
[ProducesResponseType<ShoppingError>(404)]
[ProducesResponseType<ShoppingError>(409)]
[ProducesResponseType<ShoppingError>(503)]
public abstract class ShoppingControllerBase(ShoppingOwnerResolver resolver) : ControllerBase
{
    protected async Task<IActionResult> Run(bool requireAccount, Func<ShoppingOwner, Task<ShoppingResult>> action,
        CancellationToken ct, bool write = false, string? csrf = null)
    {
        Response.Headers.CacheControl = "no-store";
        if (write && csrf != "XMLHttpRequest") return Result(ShoppingResult.Error(403, "csrf_header_required", "Żądanie zostało odrzucone."));
        try
        {
            var owner = await resolver.ResolveAsync(Request, Response, requireAccount, ct);
            if (owner.Status == 401) return Result(ShoppingResult.Error(401, "unauthenticated", "Wymagane logowanie."));
            if (owner.Status != 200 || owner.Owner is null) return Unavailable();
            return Result(await action(owner.Owner));
        }
        catch (NpgsqlException) { return Unavailable(); }
        catch (TimeoutException) { return Unavailable(); }
        catch (DbUpdateException e) when (e.InnerException is NpgsqlException) { return Unavailable(); }
    }

    private IActionResult Result(ShoppingResult result) => result.Status == 204 ? NoContent() : StatusCode(result.Status, result.Body);
    private IActionResult Unavailable() => Result(ShoppingResult.Error(503, "shopping_unavailable", "Wybór klienta jest chwilowo niedostępny."));
}
