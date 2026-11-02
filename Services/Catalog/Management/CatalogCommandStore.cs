using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed record CatalogChange(CatalogCommandResponse Response, int Status = 200,
    string? Reason = null, int? QuantityBefore = null, int? QuantityAfter = null);

public sealed class CatalogCommandStore(AppDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CatalogResult> ExecuteAsync(string kind, Guid id, long? version, object normalized,
        Guid actor, Guid key, Func<Task<CatalogChange>> change, CancellationToken ct)
    {
        if (actor == Guid.Empty || key == Guid.Empty) throw new CatalogValidation("invalid_operation_key");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { format = "catalog/v1", kind, id, version, body = normalized }, Json))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await CatalogWriteLock.AcquireAsync(db, ct);
            var old = await db.CatalogOperations.AsNoTracking().SingleOrDefaultAsync(o => o.ActorUserId == actor && o.OperationKey == key, ct);
            if (old is not null)
            {
                if (old.RequestHash != hash) throw new CatalogValidation("idempotency_key_conflict", status: 409);
                return old.StatusCode == 204 ? new(204, null) : new(200,
                    JsonSerializer.Deserialize<CatalogCommandResponse>(old.ResponseJson, Json)! with { Replayed = true });
            }
            var result = await change();
            db.CatalogOperations.Add(new CatalogOperation
            {
                Kind = kind, TargetId = result.Response.Id, ActorUserId = actor, OperationKey = key,
                RequestHash = hash, ExpectedVersion = version, ResponseJson = JsonSerializer.Serialize(result.Response, Json),
                StatusCode = result.Status, CreatedAt = clock.GetUtcNow().UtcDateTime,
                Reason = result.Reason, QuantityBefore = result.QuantityBefore, QuantityAfter = result.QuantityAfter
            });
            await db.SaveChangesAsync(ct);
            await new DynamicPromotionEngine(db, clock).ReconcileAsync(ct);
            await tx.CommitAsync(ct); // Deferred graph/publication constraints also fail here.
            return new(result.Status, result.Status == 204 ? null : result.Response);
        }
        catch (PromotionOverlapException e)
        {
            await tx.RollbackAsync(ct); await tx.DisposeAsync(); db.ChangeTracker.Clear();
            await new DynamicPromotionEngine(db, clock).RecordBlockedAsync(e, ct);
            return new(409, new CatalogError("active_promotion", "Zmiana powoduje konflikt promocji. Sprawdź panel konfliktów."));
        }
        catch (DbUpdateConcurrencyException) { return new(409, new CatalogError("version_conflict", "Dane zostały zmienione. Odczytaj aktualną wersję.")); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException p && IsRule(p)) { return Rule((PostgresException)e.InnerException!); }
        catch (PostgresException e) when (IsRule(e)) { return Rule(e); }
        finally
        {
            // Requests may be exercised repeatedly in the same test scope. Do not
            // leave Added entities behind after rollback or stale trigger versions.
            db.ChangeTracker.Clear();
        }
    }

    private static bool IsRule(PostgresException e) => e.SqlState is "23505" or "23503" or "23514" or "P0001" or "40001" or "40P01";
    private static CatalogResult Rule(PostgresException e)
    {
        var code = e.ConstraintName switch
        {
            "catalog_product_publication" => "product_not_ready",
            "catalog_variant_publication" => "missing_current_pln_price",
            "catalog_variant_identity" => "variant_configuration_locked",
            "catalog_pair_identity" => "attribute_pair_immutable",
            "catalog_key_identity" => "attribute_code_immutable",
            "dynamic_promotion_overlap" => "active_promotion",
            _ => e.SqlState == "23505" ? "unique_value_conflict" : e.SqlState == "23503" ? "resource_in_use" : "catalog_rule_conflict"
        };
        return new(409, new CatalogError(code, "Zmiana narusza reguły katalogu lub jest nieaktualna."));
    }
    public static void Version(long actual, long expected)
    {
        if (expected < 1) throw new CatalogValidation("invalid_version", "expectedVersion");
        if (actual != expected) throw new CatalogValidation("version_conflict", "expectedVersion", 409);
    }
    public static T Found<T>(T? value, string resource) where T : class => value ?? throw new CatalogValidation(resource + "_not_found", status: 404);
}
