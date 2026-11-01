using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E_commerce_backend.Contracts.Pricing;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Pricing;

public sealed class PromotionService(AppDbContext database, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static DateTime Precise(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
    private static PromotionResult Error(int status, string code) => new(status, new PromotionError(code, code));
    private static bool PageValid(int page, int size) => page > 0 && size is >= 1 and <= 100 && ((long)page - 1) * size <= int.MaxValue;

    public async Task<PromotionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await database.Promotions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? Error(404, "promotion_not_found") : new(200, Snapshot(p, await MembersAsync(id, ct)));
    }

    public async Task<PromotionResult> ListAsync(int page, int size, string? status, Guid? productId, CancellationToken ct)
        => await ListAsync(page, size, status, productId, ct, null, null);

    public async Task<PromotionResult> ListAsync(int page, int size, string? status, Guid? productId, CancellationToken ct, Guid? collectionId, string? scope)
    {
        if (!PageValid(page, size) || status is not (null or "Draft" or "Active" or "Ended") || scope is not (null or "product" or "collection") ||
            productId.HasValue && collectionId.HasValue || productId.HasValue && scope == "collection" || collectionId.HasValue && scope == "product") return Error(400, "invalid_query");
        var query = database.Promotions.AsNoTracking().Where(p => (status == null || p.Status == status) && (!productId.HasValue || p.ProductId == productId)
            && (!collectionId.HasValue || p.CollectionId == collectionId) && (scope == null || p.Scope == scope));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        var ids = rows.Select(p => p.Id).ToArray();
        var members = await MemberQuery().Where(x => ids.Contains(x.PromotionId)).ToArrayAsync(ct);
        return new(200, new PromotionListResponse(rows.Select(p => Snapshot(p, members.Where(m => m.PromotionId == p.Id).ToArray())).ToArray(), page, size, count, (int)Math.Ceiling((double)count / size)));
    }

    public async Task<PromotionResult> PreviewAsync(Guid id, int page, int size, CancellationToken ct)
    {
        if (!PageValid(page, size)) return Error(400, "invalid_pagination");
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(database, ct);
        var promotion = await database.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (promotion is null) return Error(404, "promotion_not_found");
        var preview = await BuildPreviewAsync(promotion, Precise(clock.GetUtcNow().UtcDateTime), ct);
        return new(200, new PromotionPreviewResponse(id, promotion.Version, preview.Errors.Count == 0 && preview.Items.Count > 0 && preview.Items.All(v => v.Errors.Count == 0),
            page, size, preview.Items.Count, preview.Errors,
            preview.Items.Where(v => v.ExpectedPriceChangeId.HasValue).Select(v => new ExpectedVariantPrice(v.VariantId, v.ExpectedPriceChangeId!.Value)).ToArray(),
            preview.Items.Skip((page - 1) * size).Take(size).ToArray(), preview.CollectionVersion, preview.Excluded));
    }

    public async Task<PromotionResult> MutateAsync(string kind, Guid id, PromotionDraftRequest? draft,
        PromotionVersionRequest? version, Guid key, Guid actor, CancellationToken ct)
    {
        if (key == Guid.Empty || actor == Guid.Empty || kind is not ("create" or "update" or "activate" or "end")) return Error(400, "invalid_request");
        var name = draft?.Name?.Trim();
        var type = draft?.DiscountType?.Trim().ToLowerInvariant();
        var scope = draft?.Scope?.Trim().ToLowerInvariant() ?? "product";
        if (kind is "create" or "update" && (draft is null || scope is not ("product" or "collection") ||
            (scope == "product" ? draft.ProductId is null || draft.ProductId == Guid.Empty || draft.CollectionId.HasValue : draft.CollectionId is null || draft.CollectionId == Guid.Empty || draft.ProductId.HasValue) || name is not { Length: > 0 and <= 120 } ||
            draft.DiscountValue is not decimal value || value > 9999999999999999.99m || PromotionCalculator.Calculate(9999999999999999.99m, type!, value) is null)) return Error(400, "invalid_rule");
        var expectedVersion = draft?.ExpectedVersion ?? version?.ExpectedVersion;
        if (kind != "create" && expectedVersion is not > 0 || kind == "create" && expectedVersion is not null) return Error(400, "invalid_version");
        var expected = (version as PromotionActivationRequest)?.Variants;
        var collectionVersion = (version as PromotionActivationRequest)?.ExpectedCollectionVersion;
        if (kind == "activate" && (expected is null || collectionVersion.HasValue && expected.Count > 3000 || expected.Any(v => v is null || v.VariantId == Guid.Empty || v.ExpectedPriceChangeId == Guid.Empty) || expected.Select(v => v.VariantId).Distinct().Count() != expected.Count)) return Error(400, "invalid_variant_versions");
        var serialized = JsonSerializer.Serialize(new { kind, id, productId = draft?.ProductId, name, type,
            value = draft?.DiscountValue?.ToString("G29", CultureInfo.InvariantCulture), expectedVersion,
            variants = expected?.OrderBy(v => v.VariantId).ToArray() }, Json);
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        var collectionHash = "collection/v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            kind, id, draft?.CollectionId, name, type, value = draft?.DiscountValue?.ToString("G29", CultureInfo.InvariantCulture), expectedVersion,
            collectionVersion, variants = expected?.OrderBy(v => v.VariantId).ToArray() }, Json))));
        await using var tx = await database.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(database, ct);
        var replay = await database.PromotionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.ActorUserId == actor && o.OperationKey == key, ct);
        if (replay is not null)
        {
            var isCollection = replay.RequestHash.StartsWith("collection/v1:", StringComparison.Ordinal);
            if (!isCollection && collectionVersion.HasValue || draft is not null && (scope == "collection") != isCollection ||
                replay.RequestHash != (isCollection ? collectionHash : legacyHash)) return Error(409, "idempotency_key_conflict");
            var response = JsonSerializer.Deserialize<PromotionCommandResponse>(replay.ResponseJson, Json)!;
            return new(200, response with { Replayed = true });
        }
        var now = Precise(clock.GetUtcNow().UtcDateTime);
        var p = kind == "create" ? new Promotion { ProductId = draft!.ProductId, CollectionId = draft.CollectionId, Scope = scope, Name = name!, DiscountType = type!, DiscountValue = draft.DiscountValue!.Value, CreatedByUserId = actor, CreatedAt = now, UpdatedAt = now }
            : await database.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (p is null) return Error(404, "promotion_not_found");
        if (kind != "create" && p.Version != expectedVersion) return Error(409, "promotion_version_conflict");
        if ((kind is "update" or "activate") && p.Status != "Draft" || kind == "end" && p.Status != "Active") return Error(409, "promotion_state_conflict");
        if (kind is "create" or "update")
        {
            if (scope == "product" && !await database.Products.AnyAsync(x => x.Id == draft!.ProductId, ct)) return Error(404, "product_not_found");
            if (scope == "collection" && !await database.Collections.AnyAsync(x => x.Id == draft!.CollectionId, ct)) return Error(404, "collection_not_found");
            p.ProductId = draft!.ProductId; p.CollectionId = draft.CollectionId; p.Scope = scope;
            p.Name = name!; p.DiscountType = type!; p.DiscountValue = draft.DiscountValue!.Value;
        }
        var additions = new List<object>();
        var members = kind == "create" ? [] : await MembersAsync(p.Id, ct);
        if (kind == "activate" && (p.Scope == "product" && collectionVersion.HasValue || p.Scope == "collection" && collectionVersion is not > 0)) return Error(400, "invalid_collection_version");
        var operation = new PromotionOperation { ActorUserId = actor, OperationKey = key, PromotionId = p.Id, Kind = kind, RequestHash = p.Scope == "collection" ? collectionHash : legacyHash, ResponseJson = "{}", CreatedAt = now, StatusCode = kind == "create" ? 201 : 200 };
        if (kind == "activate")
        {
            var preview = await BuildPreviewAsync(p, now, ct);
            if (p.Scope == "collection" && preview.CollectionVersion != collectionVersion) return Error(409, "collection_version_conflict");
            if (preview.Errors.Count > 0 || preview.Items.Count == 0 || preview.Items.Any(v => v.Errors.Count > 0)) return Error(409, "promotion_not_ready");
            var expectedByVariant = expected!.ToDictionary(e => e.VariantId, e => e.ExpectedPriceChangeId);
            if (expectedByVariant.Count != preview.Items.Count || preview.Items.Any(v => !expectedByVariant.TryGetValue(v.VariantId, out var priceId) || priceId != v.ExpectedPriceChangeId)) return Error(409, "price_or_scope_conflict");
            var priceIds = preview.Items.Select(v => v.ExpectedPriceChangeId!.Value).ToArray();
            var currentPrices = await database.VariantPriceChanges.AsNoTracking().Where(c => priceIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            var activatedMembers = new List<MemberRow>();
            foreach (var item in preview.Items)
            {
                var member = new PromotionVariant { PromotionId = p.Id, VariantId = item.VariantId, StartedAt = now };
                var reduction = new PriceReduction { PromotionVariantId = member.Id, VariantId = item.VariantId, PriorAmount = item.PriorAmount!.Value, ReferenceStart = item.ReferenceStart!.Value, StartedAt = now, Basis = item.ReferenceBasis! };
                var current = currentPrices[item.ExpectedPriceChangeId!.Value];
                if (current.EffectiveAt >= now) return Error(409, "price_timestamp_conflict");
                additions.Add(member); additions.Add(reduction);
                additions.Add(NewPrice(current, item.SellingAmount!.Value, reduction.Id, operation.Id, now, "Start promocji"));
                activatedMembers.Add(new MemberRow { PromotionId = p.Id, VariantId = member.VariantId, StartedAt = now, PriorAmount = reduction.PriorAmount, ReferenceStart = reduction.ReferenceStart });
            }
            members = activatedMembers.ToArray();
            p.Status = "Active"; p.ActivatedAt = now;
        }
        if (kind == "end")
        {
            var active = await database.PromotionVariants.AsNoTracking().Where(m => m.PromotionId == p.Id && m.EndedAt == null).ToArrayAsync(ct);
            if (active.Length == 0) return Error(409, "promotion_state_conflict");
            var ids = active.Select(m => m.VariantId).ToArray();
            var history = await database.VariantPriceChanges.AsNoTracking().Where(c => ids.Contains(c.VariantId) && c.Currency == "PLN").ToArrayAsync(ct);
            var pricesByVariant = history.ToLookup(c => c.VariantId);
            var memberIds = active.Select(m => m.Id).ToArray();
            var reductions = await database.PriceReductions.Where(r => memberIds.Contains(r.PromotionVariantId)).ToDictionaryAsync(r => r.PromotionVariantId, r => r.Id, ct);
            foreach (var member in active)
            {
                var current = pricesByVariant[member.VariantId].OrderByDescending(c => c.EffectiveAt).ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).FirstOrDefault();
                if (current is null || current.EffectiveAt >= now || current.PriceReductionId is null || !reductions.TryGetValue(member.Id, out var reductionId) || reductionId != current.PriceReductionId) return Error(409, "price_or_scope_conflict");
                additions.Add(NewPrice(current, current.BaseAmount, null, operation.Id, now, "Koniec promocji"));
            }
            await database.PromotionVariants.Where(m => m.PromotionId == p.Id && m.EndedAt == null).ExecuteUpdateAsync(s => s.SetProperty(m => m.EndedAt, now), ct);
            members = members.Select(m => m with { EndedAt = now }).ToArray();
            p.Status = "Ended"; p.EndedAt = now;
        }
        var oldVersion = p.Version;
        if (kind != "create") p.Version++;
        p.UpdatedAt = now;
        var body = new PromotionCommandResponse(false, Snapshot(p, members));
        operation.ResponseJson = JsonSerializer.Serialize(body, Json);
        additions.Add(operation);
        if (kind == "create") additions.Add(p);
        try
        {
            if (kind != "create")
                await database.Promotions.Where(x => x.Id == p.Id && x.Version == oldVersion).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ProductId, p.ProductId).SetProperty(x => x.CollectionId, p.CollectionId).SetProperty(x => x.Scope, p.Scope)
                    .SetProperty(x => x.Name, p.Name).SetProperty(x => x.DiscountType, p.DiscountType)
                    .SetProperty(x => x.DiscountValue, p.DiscountValue).SetProperty(x => x.Status, p.Status).SetProperty(x => x.Version, p.Version)
                    .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.ActivatedAt, p.ActivatedAt).SetProperty(x => x.EndedAt, p.EndedAt), ct);
            database.AddRange(additions);
            await database.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(operation.StatusCode, body);
        }
        finally { foreach (var entry in additions) database.Entry(entry).State = EntityState.Detached; }
    }

    private sealed record PreviewData(List<PromotionPreviewVariant> Items, List<string> Errors, long? CollectionVersion, List<PromotionExclusion> Excluded);

    private async Task<PreviewData> BuildPreviewAsync(Promotion p, DateTime now, CancellationToken ct)
    {
        var errors = new List<string>();
        var excluded = new List<PromotionExclusion>();
        long? collectionVersion = null;
        if (p.Status != "Draft") errors.Add("promotion_not_draft");
        var products = database.Products.AsNoTracking();
        if (p.Scope == "product")
        {
            products = products.Where(x => x.Id == p.ProductId);
            if (!await products.AnyAsync(x => x.Status == CatalogStatus.Published && x.ProductCategories.Any(), ct)) errors.Add("product_not_visible");
        }
        else
        {
            var collection = await database.Collections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == p.CollectionId, ct);
            collectionVersion = collection?.Version;
            if (collection?.Status != "Published") errors.Add("collection_not_visible");
            products = products.Where(x => database.CollectionProducts.Any(l => l.CollectionId == p.CollectionId && l.ProductId == x.Id));
            var excludedProducts = await products.Where(x => x.Status != CatalogStatus.Published || !x.ProductCategories.Any())
                .Select(x => x.Id).ToArrayAsync(ct);
            excluded.AddRange(excludedProducts.Select(id => new PromotionExclusion(id, null, "product_not_visible")));
            products = products.Where(x => x.Status == CatalogStatus.Published && x.ProductCategories.Any());
        }
        var query = database.ProductVariants.AsNoTracking().Where(v => products.Any(x => x.Id == v.ProductId));
        excluded.AddRange(await query.Where(v => v.Status != CatalogStatus.Published)
            .Select(v => new PromotionExclusion(v.ProductId, v.Id, "variant_not_visible")).ToArrayAsync(ct));
        var candidates = query.Where(v => v.Status == CatalogStatus.Published);
        if (p.Scope == "collection" && await candidates.CountAsync(ct) > 3000) return new([], [.. errors, "variant_limit_exceeded"], collectionVersion, excluded);
        var variants = await candidates.OrderBy(v => v.Id).Select(v => new { v.Id, v.SKU, v.ProductId, ProductName = v.Product.Name }).ToArrayAsync(ct);
        if (variants.Length == 0) errors.Add("no_variants");
        var ids = variants.Select(v => v.Id).ToArray();
        var prices = await database.VariantPriceChanges.AsNoTracking().Where(c => ids.Contains(c.VariantId) && c.Currency == "PLN").ToArrayAsync(ct);
        var events = await database.VariantOfferEvents.AsNoTracking().Where(e => ids.Contains(e.VariantId)).ToArrayAsync(ct);
        var active = await database.PromotionVariants.Where(m => ids.Contains(m.VariantId) && m.EndedAt == null).Select(m => m.VariantId).ToArrayAsync(ct);
        var priceGroups = prices.ToLookup(c => c.VariantId); var offerGroups = events.ToLookup(e => e.VariantId);
        var items = new List<PromotionPreviewVariant>();
        foreach (var v in variants)
        {
            var problems = new List<string>();
            var history = priceGroups[v.Id].ToArray();
            var current = history.Where(c => c.EffectiveAt <= now).OrderByDescending(c => c.EffectiveAt).ThenByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).FirstOrDefault();
            if (current is null) problems.Add("missing_price");
            if (history.Any(c => c.EffectiveAt > now)) problems.Add("future_price");
            if (active.Contains(v.Id)) problems.Add("active_promotion");
            if (current is not null && current.BaseAmount != current.SellingAmount) problems.Add("unmanaged_discount");
            var amount = current is null ? null : PromotionCalculator.Calculate(current.BaseAmount, p.DiscountType, p.DiscountValue);
            if (amount is null) problems.Add("invalid_discount");
            var reference = PriorPriceCalculator.Calculate(history, offerGroups[v.Id], now);
            if (reference is null) problems.Add("incomplete_or_empty_offer_history");
            if (amount is not null && reference is not null && amount >= reference.Amount) problems.Add("not_below_prior_price");
            items.Add(new(v.Id, v.SKU, current?.Id, current?.BaseAmount, amount, reference?.Amount, reference?.ReferenceStart, reference?.Basis, problems, v.ProductId, v.ProductName));
        }
        return new(items, errors, collectionVersion, excluded);
    }

    private static VariantPriceChange NewPrice(VariantPriceChange previous, decimal amount, Guid? reduction,
        Guid operation, DateTime now, string reason) => new() { VariantId = previous.VariantId, Currency = "PLN",
            BaseAmount = previous.BaseAmount, SellingAmount = amount, EffectiveAt = now, CreatedAt = now,
            Reason = reason, PreviousPriceChangeId = previous.Id, PromotionOperationId = operation, PriceReductionId = reduction };

    private sealed record MemberRow
    {
        public Guid PromotionId { get; init; }
        public Guid VariantId { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime? EndedAt { get; init; }
        public decimal? PriorAmount { get; init; }
        public DateTime? ReferenceStart { get; init; }
    }
    private IQueryable<MemberRow> MemberQuery() => from m in database.PromotionVariants.AsNoTracking()
        join r in database.PriceReductions on m.Id equals r.PromotionVariantId into reductions
        from r in reductions.DefaultIfEmpty()
        select new MemberRow { PromotionId = m.PromotionId, VariantId = m.VariantId, StartedAt = m.StartedAt, EndedAt = m.EndedAt, PriorAmount = r == null ? null : r.PriorAmount, ReferenceStart = r == null ? null : r.ReferenceStart };
    private Task<MemberRow[]> MembersAsync(Guid id, CancellationToken ct) => MemberQuery().Where(m => m.PromotionId == id).ToArrayAsync(ct);
    private static PromotionResponse Snapshot(Promotion p, MemberRow[] members) => new(p.Id, p.ProductId, p.Name, p.DiscountType, p.DiscountValue, "PLN", p.Status, p.Version, p.CreatedAt, p.UpdatedAt,
        p.ActivatedAt, p.EndedAt, members.OrderBy(m => m.VariantId).Select(m => new PromotionMemberResponse(m.VariantId, m.StartedAt, m.EndedAt, m.PriorAmount, m.ReferenceStart)).ToArray(), p.Scope, p.CollectionId);
}
