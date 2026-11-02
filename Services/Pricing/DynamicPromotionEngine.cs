using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public sealed record PromotionClaim(Guid PromotionId, Guid VariantId);
public sealed class PromotionOverlapException(IReadOnlyList<PromotionClaim> claims) : Exception("active_promotion")
{
    public IReadOnlyList<PromotionClaim> Claims { get; } = claims;
}

/// <summary>Caller owns the transaction and CatalogWriteLock. Legacy scopes remain frozen.</summary>
public sealed class DynamicPromotionEngine(AppDbContext db, TimeProvider clock)
{
    public static bool IsDynamic(string? scope) => scope is "catalog" or "category";
    public static DateTime Precise(DateTime at) => new(at.Ticks - at.Ticks % 10, DateTimeKind.Utc);

    public Task<Guid[]> TargetsAsync(Guid id, CancellationToken ct) => db.Database.SqlQuery<Guid>(
        $"SELECT variant_id AS \"Value\" FROM app.dynamic_promotion_targets WHERE promotion_id={id}").ToArrayAsync(ct);

    public Task<PromotionClaim[]> ClaimsAsync(CancellationToken ct) => db.Database.SqlQuery<PromotionClaim>(
        $"SELECT promotion_id AS \"PromotionId\", variant_id AS \"VariantId\" FROM app.active_promotion_claims").ToArrayAsync(ct);

    public async Task CheckOverlapsAsync(CancellationToken ct)
    {
        var claims = await ClaimsAsync(ct);
        var collisions = claims.GroupBy(c => c.VariantId).Where(g => g.Select(c => c.PromotionId).Distinct().Count() > 1).SelectMany(g => g).ToArray();
        if (collisions.Length > 0) throw new PromotionOverlapException(collisions);
    }

    public async Task<List<PromotionPreviewVariant>> AssessAsync(Promotion promotion, CancellationToken ct, DateTime? assessedAt = null)
    {
        var now = assessedAt ?? Precise(clock.GetUtcNow().UtcDateTime);
        var ids = await TargetsAsync(promotion.Id, ct);
        var rows = await db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id))
            .OrderBy(v => v.Id).Select(v => new { v.Id, v.SKU, v.ProductId, ProductName = v.Product.Name }).ToArrayAsync(ct);
        var prices = (await db.VariantPriceChanges.AsNoTracking().Where(p => ids.Contains(p.VariantId) && p.Currency == "PLN").ToArrayAsync(ct)).ToLookup(p => p.VariantId);
        var events = (await db.VariantOfferEvents.AsNoTracking().Where(e => ids.Contains(e.VariantId)).ToArrayAsync(ct)).ToLookup(e => e.VariantId);
        var claims = (await ClaimsAsync(ct)).Where(c => c.PromotionId != promotion.Id).ToLookup(c => c.VariantId);
        var own = (await db.PromotionVariants.Where(m => m.PromotionId == promotion.Id && m.EndedAt == null).Select(m => m.VariantId).ToArrayAsync(ct)).ToHashSet();
        var ownReductions = await (from m in db.PromotionVariants join r in db.PriceReductions on m.Id equals r.PromotionVariantId
            where m.PromotionId == promotion.Id && m.EndedAt == null select new { m.VariantId, r.Id }).ToDictionaryAsync(x => x.VariantId, x => x.Id, ct);
        var result = new List<PromotionPreviewVariant>(rows.Length);
        foreach (var v in rows)
        {
            var history = prices[v.Id].ToArray();
            var current = history.Where(p => p.EffectiveAt <= now).OrderByDescending(p => p.EffectiveAt).ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).FirstOrDefault();
            var errors = new List<string>();
            if (current is null) errors.Add("missing_price");
            if (history.Any(p => p.EffectiveAt > now)) errors.Add("future_price");
            if (claims.Contains(v.Id)) errors.Add("active_promotion");
            var alreadyApplied = own.Contains(v.Id);
            if (alreadyApplied && (current is null || !ownReductions.TryGetValue(v.Id, out var reductionId) || current.PriceReductionId != reductionId)) errors.Add("price_chain_conflict");
            if (!alreadyApplied && current is not null && current.BaseAmount != current.SellingAmount) errors.Add("unmanaged_discount");
            var amount = current is null ? null : PromotionCalculator.Calculate(current.BaseAmount, promotion.DiscountType, promotion.DiscountValue);
            if (amount is null) errors.Add("invalid_discount");
            var reference = alreadyApplied ? null : PriorPriceCalculator.Calculate(history, events[v.Id], now);
            if (!alreadyApplied && reference is null) errors.Add("incomplete_or_empty_offer_history");
            if (!alreadyApplied && amount is not null && reference is not null && amount >= reference.Amount) errors.Add("not_below_prior_price");
            if (!alreadyApplied && current is not null && current.EffectiveAt >= now) errors.Add("price_timestamp_conflict");
            result.Add(new(v.Id, v.SKU, current?.Id, current?.BaseAmount, amount, reference?.Amount, reference?.ReferenceStart,
                reference?.Basis, errors, v.ProductId, v.ProductName));
        }
        return result;
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Promotion reconciliation requires a transaction and catalog lock.");
        await CheckOverlapsAsync(ct);
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.Status == "Active" && (p.Scope == "catalog" || p.Scope == "category")).OrderBy(p => p.Id).ToArrayAsync(ct);
        // Departures must finish before arrivals, including moves between two disjoint categories.
        foreach (var p in promotions)
        {
            var targets = await TargetsAsync(p.Id, ct);
            await RemoveMembersAsync(p, targets, ct);
        }
        foreach (var p in promotions)
        {
            var assessedAt = Precise(clock.GetUtcNow().UtcDateTime);
            var items = await AssessAsync(p, ct, assessedAt);
            var existing = (await db.PromotionVariants.Where(m => m.PromotionId == p.Id && m.EndedAt == null).Select(m => m.VariantId).ToArrayAsync(ct)).ToHashSet();
            var ready = items.Where(i => i.Errors.Count == 0 && !existing.Contains(i.VariantId)).ToArray();
            if (ready.Length > 0)
            {
                var now = assessedAt;
                var priceIds = ready.Select(i => i.ExpectedPriceChangeId!.Value).ToArray();
                var current = await db.VariantPriceChanges.AsNoTracking().Where(c => priceIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
                var operation = SystemOperation(p.Id, now);
                await SaveAsync([operation], ct);
                var memberIds = ready.Select(_ => Guid.NewGuid()).ToArray();
                var variantIds = ready.Select(i => i.VariantId).ToArray();
                var reductionIds = ready.Select(_ => Guid.NewGuid()).ToArray();
                var priorAmounts = ready.Select(i => i.PriorAmount!.Value).ToArray();
                var referenceStarts = ready.Select(i => i.ReferenceStart!.Value).ToArray();
                var bases = ready.Select(i => i.ReferenceBasis!).ToArray();
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.promotion_variants(id,promotion_id,variant_id,started_at) SELECT u.id,{p.Id},u.variant_id,{now} FROM unnest({memberIds},{variantIds}) AS u(id,variant_id)", ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.price_reductions(id,promotion_variant_id,variant_id,prior_amount,reference_start,started_at,basis) SELECT u.id,u.member_id,u.variant_id,u.amount,u.reference_start,{now},u.basis FROM unnest({reductionIds},{memberIds},{variantIds},{priorAmounts},{referenceStarts},{bases}) AS u(id,member_id,variant_id,amount,reference_start,basis)", ct);
                await InsertPricesAsync(ready.Select(i => current[i.ExpectedPriceChangeId!.Value]).ToArray(), ready.Select(i => i.SellingAmount!.Value).ToArray(), reductionIds, operation.Id, now, "Dołączenie do promocji", ct);
                await AdvanceVersionAsync(p.Id, now, ct);
            }
            await SyncConflictsAsync(p.Id, items, ct);
        }
        // Ending an action closes its outstanding diagnostic entries.
        var endedIds = db.Promotions.Where(p => p.Status == "Ended").Select(p => p.Id);
        var resolvedAt = Precise(clock.GetUtcNow().UtcDateTime);
        await db.PromotionConflicts.Where(c => c.ResolvedAt == null && endedIds.Contains(c.PromotionId))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ResolvedAt, resolvedAt).SetProperty(c => c.UpdatedAt, resolvedAt), ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.promotion_reconciliation (id,dirty,next_attempt_at) VALUES (1,false,{resolvedAt.AddMinutes(1)}) ON CONFLICT (id) DO UPDATE SET dirty=false,next_attempt_at=EXCLUDED.next_attempt_at", ct);
    }

    public async Task RemoveMembersAsync(Promotion p, Guid[] retain, CancellationToken ct)
    {
        var members = await db.PromotionVariants.AsNoTracking().Where(m => m.PromotionId == p.Id && m.EndedAt == null && !retain.Contains(m.VariantId)).ToArrayAsync(ct);
        if (members.Length == 0) return;
        var ids = members.Select(m => m.VariantId).ToArray();
        var allPrices = (await db.VariantPriceChanges.AsNoTracking().Where(c => ids.Contains(c.VariantId) && c.Currency == "PLN").ToArrayAsync(ct)).ToLookup(c => c.VariantId);
        var memberIds = members.Select(m => m.Id).ToArray();
        var reductions = await db.PriceReductions.AsNoTracking().Where(r => memberIds.Contains(r.PromotionVariantId)).ToDictionaryAsync(r => r.PromotionVariantId, r => r.Id, ct);
        var now = Precise(clock.GetUtcNow().UtcDateTime);
        var operation = SystemOperation(p.Id, now);
        var previousPrices = new List<VariantPriceChange>(members.Length);
        foreach (var member in members)
        {
            var current = allPrices[member.VariantId].OrderByDescending(c => c.EffectiveAt).ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).FirstOrDefault();
            if (current is null || current.EffectiveAt >= now || current.PriceReductionId is not Guid rid || !reductions.TryGetValue(member.Id, out var expected) || rid != expected)
                throw new InvalidOperationException("Cannot restore a price outside its promotion chain.");
            previousPrices.Add(current);
        }
        await db.PromotionVariants.Where(m => memberIds.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.EndedAt, now), ct);
        await SaveAsync([operation], ct);
        await InsertPricesAsync(previousPrices.ToArray(), previousPrices.Select(c => c.BaseAmount).ToArray(), null, operation.Id, now, "Wyjście z promocji", ct);
        await AdvanceVersionAsync(p.Id, now, ct);
    }

    public async Task SyncConflictsAsync(Guid promotionId, IReadOnlyList<PromotionPreviewVariant> items, CancellationToken ct)
    {
        var open = await db.PromotionConflicts.AsNoTracking().Where(c => c.PromotionId == promotionId && c.ResolvedAt == null).ToArrayAsync(ct);
        var now = Precise(clock.GetUtcNow().UtcDateTime);
        var desired = items.SelectMany(i => i.Errors.Select(code => (i.VariantId, Code: code))).ToHashSet();
        var openKeys = open.Select(c => (c.VariantId, c.Code)).ToHashSet();
        var resolved = open.Where(c => !desired.Contains((c.VariantId, c.Code))).Select(c => c.Id).ToArray();
        await db.PromotionConflicts.Where(c => resolved.Contains(c.Id)).ExecuteUpdateAsync(s => s.SetProperty(c => c.ResolvedAt, now).SetProperty(c => c.UpdatedAt, now), ct);
        var claims = desired.Any(d => d.Code == "active_promotion") ? await ClaimsAsync(ct) : [];
        var additions = desired.Where(d => !openKeys.Contains(d)).Select(d => (object)new PromotionConflict
        {
            PromotionId = promotionId, VariantId = d.VariantId, Code = d.Code, CreatedAt = now, UpdatedAt = now,
            OtherPromotionId = claims.Where(c => c.VariantId == d.VariantId && c.PromotionId != promotionId).Select(c => (Guid?)c.PromotionId).FirstOrDefault()
        }).ToList();
        if (additions.Count > 0) await SaveAsync(additions, ct);
    }

    // Failed business changes roll back their transaction before this separate diagnostic transaction.
    public async Task RecordBlockedAsync(PromotionOverlapException failure, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(db, ct);
        var now = Precise(clock.GetUtcNow().UtcDateTime);
        foreach (var claim in failure.Claims.Distinct())
        {
            var other = failure.Claims.First(c => c.VariantId == claim.VariantId && c.PromotionId != claim.PromotionId).PromotionId;
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.promotion_conflicts (id,promotion_id,variant_id,other_promotion_id,code,created_at,updated_at) VALUES ({id},{claim.PromotionId},{claim.VariantId},{other},'active_promotion',{now},{now}) ON CONFLICT (promotion_id,variant_id,code) WHERE resolved_at IS NULL DO UPDATE SET updated_at=EXCLUDED.updated_at,other_promotion_id=EXCLUDED.other_promotion_id", ct);
        }
        await tx.CommitAsync(ct);
    }

    public Task AdvanceVersionAsync(Guid id, DateTime now, CancellationToken ct) => db.Promotions.Where(p => p.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Version, p => p.Version + 1).SetProperty(p => p.UpdatedAt, now), ct);

    private async Task SaveAsync(List<object> entries, CancellationToken ct)
    {
        db.AddRange(entries);
        try { await db.SaveChangesAsync(ct); }
        finally { foreach (var entry in entries) db.Entry(entry).State = EntityState.Detached; }
    }

    private static PromotionOperation SystemOperation(Guid id, DateTime now) => new()
    { PromotionId = id, Kind = "dynamic_reconcile", RequestHash = "dynamic-reconcile/v1", ResponseJson = "{}", StatusCode = 200, CreatedAt = now };

    private async Task InsertPricesAsync(VariantPriceChange[] previous, decimal[] amounts, Guid[]? reductions, Guid operation, DateTime now, string reason, CancellationToken ct)
    {
        var ids = previous.Select(_ => Guid.NewGuid()).ToArray();
        var variants = previous.Select(c => c.VariantId).ToArray();
        var baseAmounts = previous.Select(c => c.BaseAmount).ToArray();
        var previousIds = previous.Select(c => c.Id).ToArray();
        if (reductions is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.variant_price_changes(id,variant_id,base_amount,selling_amount,currency,effective_at,created_at,reason,previous_price_change_id,promotion_operation_id,price_reduction_id) SELECT u.id,u.variant_id,u.base_amount,u.amount,'PLN',{now},{now},{reason},u.previous_id,{operation},u.reduction_id FROM unnest({ids},{variants},{baseAmounts},{amounts},{previousIds},{reductions}) AS u(id,variant_id,base_amount,amount,previous_id,reduction_id)", ct);
        else
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO app.variant_price_changes(id,variant_id,base_amount,selling_amount,currency,effective_at,created_at,reason,previous_price_change_id,promotion_operation_id) SELECT u.id,u.variant_id,u.base_amount,u.amount,'PLN',{now},{now},{reason},u.previous_id,{operation} FROM unnest({ids},{variants},{baseAmounts},{amounts},{previousIds}) AS u(id,variant_id,base_amount,amount,previous_id)", ct);
    }
}
