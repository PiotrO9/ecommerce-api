using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public sealed class DynamicPromotionService(AppDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static PromotionResult Error(int status, string code) => new(status, new PromotionError(code, code));
    private static bool PageValid(int page, int size) => page > 0 && size is >= 1 and <= 100 && ((long)page - 1) * size <= int.MaxValue;

    public static string Token(Promotion p, IReadOnlyList<PromotionPreviewVariant> items) => Hash(JsonSerializer.Serialize(new
    { p.Id, p.Version, items = items.OrderBy(i => i.VariantId).Select(i => new { i.VariantId, i.ExpectedPriceChangeId, i.SellingAmount, i.PriorAmount, i.Errors }) }, Json));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<PromotionResult> PreviewAsync(Promotion p, int page, int size, CancellationToken ct)
    {
        var items = await new DynamicPromotionEngine(db, clock).AssessAsync(p, ct);
        var errors = new List<string>();
        if (p.Status != "Draft") errors.Add("promotion_not_draft");
        if (items.Count == 0) errors.Add("no_variants");
        if (items.Any(i => i.Errors.Contains("active_promotion"))) errors.Add("active_promotion");
        return new(200, new PromotionPreviewResponse(p.Id, p.Version, errors.Count == 0,
            page, size, items.Count, errors, [], items.Skip((page - 1) * size).Take(size).ToArray(),
            ScopeToken: Token(p, items), EligibleVariants: items.Count(i => i.Errors.Count == 0), ConflictedVariants: items.Count(i => i.Errors.Count > 0)));
    }

    public async Task<PromotionResponse> ResponseAsync(Promotion p, CancellationToken ct) => new(p.Id, p.ProductId, p.Name, p.DiscountType,
        p.DiscountValue, "PLN", p.Status, p.Version, p.CreatedAt, p.UpdatedAt, p.ActivatedAt, p.EndedAt, [], p.Scope, p.CollectionId,
        p.CategoryId, await db.PromotionVariants.CountAsync(m => m.PromotionId == p.Id && m.EndedAt == null, ct));

    public async Task<PromotionResult> ExclusionsAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (p is null) return Error(404, "promotion_not_found");
        if (!DynamicPromotionEngine.IsDynamic(p.Scope)) return Error(400, "dynamic_scope_required");
        var rows = await db.PromotionExclusions.AsNoTracking().Where(e => e.PromotionId == id).ToArrayAsync(ct);
        return new(200, new PromotionExclusionsResponse(id, p.Version, rows.Where(e => e.ProductId.HasValue).Select(e => e.ProductId!.Value).Order().ToArray(),
            rows.Where(e => e.VariantId.HasValue).Select(e => e.VariantId!.Value).Order().ToArray()));
    }

    public async Task<PromotionResult> ConflictsAsync(Guid? id, bool resolved, int page, int size, CancellationToken ct)
    {
        if (!PageValid(page, size)) return Error(400, "invalid_pagination");
        if (id.HasValue && !await db.Promotions.AnyAsync(p => p.Id == id, ct)) return Error(404, "promotion_not_found");
        var query = db.PromotionConflicts.AsNoTracking().Where(c => (!id.HasValue || c.PromotionId == id) && (resolved || c.ResolvedAt == null));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).Skip((page - 1) * size).Take(size)
            .Select(c => new PromotionConflictRow(c.Id, c.PromotionId, c.VariantId, c.OtherPromotionId, c.Code, c.CreatedAt, c.UpdatedAt, c.ResolvedAt)).ToArrayAsync(ct);
        return new(200, new PromotionConflictPage(rows, page, size, count, (int)Math.Ceiling((double)count / size)));
    }

    public async Task<PromotionResult> MembersAsync(Guid id, int page, int size, CancellationToken ct)
    {
        if (!PageValid(page, size)) return Error(400, "invalid_pagination");
        if (!await db.Promotions.AnyAsync(p => p.Id == id, ct)) return Error(404, "promotion_not_found");
        var query = from m in db.PromotionVariants.AsNoTracking() join r in db.PriceReductions on m.Id equals r.PromotionVariantId into reductions
                    from r in reductions.DefaultIfEmpty() where m.PromotionId == id
                    select new { m.Id, m.VariantId, m.StartedAt, m.EndedAt, PriorAmount = r == null ? (decimal?)null : r.PriorAmount, ReferenceStart = r == null ? (DateTime?)null : r.ReferenceStart };
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(m => m.StartedAt).ThenBy(m => m.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        return new(200, new PromotionMemberPage(rows.Select(m => new PromotionMemberResponse(m.VariantId, m.StartedAt, m.EndedAt, m.PriorAmount, m.ReferenceStart)).ToArray(), page, size, count, (int)Math.Ceiling((double)count / size)));
    }

    public async Task<PromotionResult> MutateAsync(string kind, Guid id, PromotionDraftRequest? draft, PromotionVersionRequest? request,
        Guid key, Guid actor, CancellationToken ct)
    {
        var scope = draft?.Scope?.Trim().ToLowerInvariant();
        var name = draft?.Name?.Trim(); var type = draft?.DiscountType?.Trim().ToLowerInvariant();
        var exclusions = request as PromotionExclusionsRequest;
        var products = (exclusions?.ProductIds ?? draft?.ExcludedProductIds ?? []).Distinct().Order().ToArray();
        var variants = (exclusions?.VariantIds ?? draft?.ExcludedVariantIds ?? []).Distinct().Order().ToArray();
        var version = draft?.ExpectedVersion ?? request?.ExpectedVersion;
        if (key == Guid.Empty || actor == Guid.Empty || kind is not ("create" or "update" or "activate" or "end" or "exclusions") ||
            products.Contains(Guid.Empty) || variants.Contains(Guid.Empty)) return Error(400, "invalid_request");
        if (kind == "create" ? version.HasValue : version is not > 0) return Error(400, "invalid_version");
        if (kind is "create" or "update" && (draft is null || !DynamicPromotionEngine.IsDynamic(scope) || draft.ProductId.HasValue || draft.CollectionId.HasValue ||
            (scope == "category" ? draft.CategoryId is null || draft.CategoryId == Guid.Empty : draft.CategoryId.HasValue) ||
            name is not { Length: > 0 and <= 120 } || draft.DiscountValue is not decimal value || value > 9999999999999999.99m ||
            PromotionCalculator.Calculate(9999999999999999.99m, type!, value) is null)) return Error(400, "invalid_rule");
        if (kind == "exclusions" && exclusions is null) return Error(400, "invalid_request");
        var activation = request as PromotionActivationRequest;
        if (kind == "activate" && (activation?.ExpectedScopeToken is not { Length: 64 } || activation.ExpectedCollectionVersion.HasValue || activation.Variants is { Count: > 0 }))
            return Error(400, "invalid_scope_token");
        var hash = "dynamic/v1:" + Hash(JsonSerializer.Serialize(new { kind, id, scope, categoryId = draft?.CategoryId, name, type,
            value = draft?.DiscountValue?.ToString("G29", CultureInfo.InvariantCulture), version, products, variants, token = activation?.ExpectedScopeToken }, Json));
        var engine = new DynamicPromotionEngine(db, clock);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await CatalogWriteLock.AcquireAsync(db, ct);
            var replay = await db.PromotionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.ActorUserId == actor && o.OperationKey == key, ct);
            if (replay is not null)
            {
                if (replay.RequestHash != hash) return Error(409, "idempotency_key_conflict");
                return new(200, JsonSerializer.Deserialize<PromotionCommandResponse>(replay.ResponseJson, Json)! with { Replayed = true });
            }
            var now = DynamicPromotionEngine.Precise(clock.GetUtcNow().UtcDateTime);
            var p = kind == "create" ? new Promotion { Scope = scope!, CategoryId = draft!.CategoryId, Name = name!, DiscountType = type!,
                DiscountValue = draft.DiscountValue!.Value, CreatedByUserId = actor, CreatedAt = now, UpdatedAt = now }
                : await db.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (p is null) return Error(404, "promotion_not_found");
            if (!DynamicPromotionEngine.IsDynamic(p.Scope)) return Error(400, "dynamic_scope_required");
            if (kind != "create" && p.Version != version) return Error(409, "promotion_version_conflict");
            if ((kind is "update" or "activate") && p.Status != "Draft" || kind == "end" && p.Status != "Active" || kind == "exclusions" && p.Status == "Ended")
                return Error(409, "promotion_state_conflict");
            if (kind is "create" or "update" or "exclusions")
            {
                if (draft?.CategoryId is Guid category && !await db.Categories.AnyAsync(c => c.Id == category, ct)) return Error(404, "category_not_found");
                if (await db.Products.CountAsync(p => products.Contains(p.Id), ct) != products.Length || await db.ProductVariants.CountAsync(v => variants.Contains(v.Id), ct) != variants.Length)
                    return Error(404, "exclusion_target_not_found");
                if (kind == "create")
                {
                    db.Promotions.Add(p); await db.SaveChangesAsync(ct); db.Entry(p).State = EntityState.Detached;
                }
                else if (kind == "update")
                {
                    await db.Promotions.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Scope, scope!)
                        .SetProperty(x => x.CategoryId, draft!.CategoryId).SetProperty(x => x.Name, name!)
                        .SetProperty(x => x.DiscountType, type!).SetProperty(x => x.DiscountValue, draft!.DiscountValue!.Value), ct);
                }
                await db.PromotionExclusions.Where(e => e.PromotionId == p.Id).ExecuteDeleteAsync(ct);
                var rules = products.Select(product => new PromotionExclusionRule { PromotionId = p.Id, ProductId = product, CreatedAt = now })
                    .Concat(variants.Select(variant => new PromotionExclusionRule { PromotionId = p.Id, VariantId = variant, CreatedAt = now })).ToArray();
                db.PromotionExclusions.AddRange(rules); await db.SaveChangesAsync(ct);
                foreach (var rule in rules) db.Entry(rule).State = EntityState.Detached;
            }
            if (kind == "activate")
            {
                var items = await engine.AssessAsync(p, ct);
                if (Token(p, items) != activation!.ExpectedScopeToken) return Error(409, "price_or_scope_conflict");
                if (items.Count == 0) return Error(409, "no_variants");
                if (items.Any(i => i.Errors.Contains("active_promotion")))
                {
                    await engine.SyncConflictsAsync(p.Id, items, ct);
                    await tx.CommitAsync(ct);
                    return Error(409, "active_promotion");
                }
                await db.Promotions.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active").SetProperty(x => x.ActivatedAt, now), ct);
            }
            if (kind == "end")
            {
                await engine.RemoveMembersAsync(p, [], ct);
                var endedAt = DynamicPromotionEngine.Precise(clock.GetUtcNow().UtcDateTime);
                await db.Promotions.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Ended").SetProperty(x => x.EndedAt, endedAt), ct);
            }
            if (kind != "create") await engine.AdvanceVersionAsync(p.Id, now, ct);
            await engine.ReconcileAsync(ct);
            p = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == p.Id, ct);
            var response = new PromotionCommandResponse(false, await ResponseAsync(p, ct));
            var operation = new PromotionOperation { PromotionId = p.Id, Kind = kind, ActorUserId = actor, OperationKey = key, RequestHash = hash,
                ResponseJson = JsonSerializer.Serialize(response, Json), StatusCode = kind == "create" ? 201 : 200, CreatedAt = now };
            db.PromotionOperations.Add(operation); await db.SaveChangesAsync(ct); db.Entry(operation).State = EntityState.Detached;
            await tx.CommitAsync(ct);
            return new(operation.StatusCode, response);
        }
        catch (PromotionOverlapException e)
        {
            await tx.RollbackAsync(ct); await tx.DisposeAsync(); db.ChangeTracker.Clear();
            await engine.RecordBlockedAsync(e, ct);
            return Error(409, "active_promotion");
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
