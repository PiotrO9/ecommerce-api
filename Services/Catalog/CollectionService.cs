using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using E_commerce_backend.Contracts.Catalog;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog;

public sealed class CollectionService(AppDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static bool ValidPage(int page, int size) => page > 0 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;
    private static CollectionResult Error(int status, string code) => new(status, new CollectionError(code, code));

    private IQueryable<CollectionResponse> Project(IQueryable<Collection> source, bool admin)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var visible = CollectionScope.VisibleProducts(db, now);
        return source.Select(c => new CollectionResponse(c.Id, c.DisplayName, c.Slug, c.Status, c.Version,
            c.CreatedAt, c.UpdatedAt, db.CollectionProducts.Count(l => l.CollectionId == c.Id &&
                (admin || visible.Any(p => p.Id == l.ProductId)))));
    }

    public async Task<CollectionResult> ListAsync(bool admin, int page, int size, string? status, CancellationToken ct)
    {
        if (!ValidPage(page, size) || status is not (null or "Draft" or "Published" or "Hidden")) return Error(400, "invalid_query");
        var query = db.Collections.AsNoTracking().Where(c => admin ? status == null || c.Status == status : c.Status == "Published");
        var count = await query.CountAsync(ct);
        var rows = await Project(query.OrderBy(c => c.DisplayName).ThenBy(c => c.Id).Skip((page - 1) * size).Take(size), admin).ToArrayAsync(ct);
        return new(200, new CollectionListResponse(rows, page, size, count, (int)Math.Ceiling((double)count / size)));
    }

    public async Task<CollectionResult> GetAsync(bool admin, Guid? id, string? slug, CancellationToken ct)
    {
        if (!CollectionScope.ValidSelector(id, slug, true)) return Error(400, "invalid_selector");
        var normalized = slug?.Trim().ToLowerInvariant();
        var query = db.Collections.AsNoTracking().Where(c => (admin || c.Status == "Published") && (id.HasValue ? c.Id == id : c.Slug == normalized));
        var row = await Project(query, admin).SingleOrDefaultAsync(ct);
        return row is null ? Error(404, "collection_not_found") : new(200, row);
    }

    public async Task<CollectionResult> MembersAsync(Guid id, int page, int size, CancellationToken ct)
    {
        if (!ValidPage(page, size)) return Error(400, "invalid_pagination");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(db, ct);
        var version = await db.Collections.Where(c => c.Id == id).Select(c => (long?)c.Version).SingleOrDefaultAsync(ct);
        if (version is null) return Error(404, "collection_not_found");
        var ids = await db.CollectionProducts.Where(l => l.CollectionId == id).OrderBy(l => l.ProductId).Select(l => l.ProductId).ToArrayAsync(ct);
        var items = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * size).Take(size).Select(p => new CollectionProductResponse(p.Id, p.Name, p.Slug, p.Status.ToString())).ToArrayAsync(ct);
        return new(200, new CollectionProductsResponse(version.Value, ids, items, page, size, ids.Length, (int)Math.Ceiling((double)ids.Length / size)));
    }

    public async Task<CollectionResult> MutateAsync(string kind, Guid id, CollectionWriteRequest? request,
        CollectionProductsRequest? members, Guid key, Guid actor, CancellationToken ct)
    {
        if (kind is not ("create" or "update" or "members") || key == Guid.Empty || actor == Guid.Empty) return Error(400, "invalid_request");
        var name = request?.DisplayName?.Trim(); var slug = request?.Slug?.Trim().ToLowerInvariant();
        var status = kind == "create" ? "Draft" : request?.Status;
        var version = request?.ExpectedVersion ?? members?.ExpectedVersion;
        if (kind is "create" or "update" && (name is not { Length: > 0 and <= 120 } || slug is not { Length: > 0 and <= 160 } ||
            !Regex.IsMatch(slug, "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant) || status is not ("Draft" or "Published" or "Hidden"))) return Error(400, "invalid_collection");
        if (kind == "create" && (version.HasValue || request!.Status is not (null or "Draft")) || kind != "create" && version is not > 0) return Error(400, "invalid_version");
        var ids = members?.ProductIds;
        if (kind == "members" && (ids is null || ids.Count > 10000 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)) return Error(400, "invalid_product_ids");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind, id, name, slug, status, version, ids = ids?.Order().ToArray() }, Json))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CatalogWriteLock.AcquireAsync(db, ct);
        var old = await db.CollectionOperations.AsNoTracking().SingleOrDefaultAsync(o => o.ActorUserId == actor && o.OperationKey == key, ct);
        if (old is not null)
        {
            if (old.RequestHash != hash) return Error(409, "idempotency_key_conflict");
            return new(200, JsonSerializer.Deserialize<CollectionCommandResponse>(old.ResponseJson, Json)! with { Replayed = true });
        }
        var c = kind == "create" ? new Collection { DisplayName = name!, Slug = slug! } : await db.Collections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (c is null) return Error(404, "collection_not_found");
        if (kind != "create" && c.Version != version) return Error(409, "collection_version_conflict");
        if (kind != "members" && await db.Collections.AnyAsync(x => x.Id != c.Id && x.Slug == slug, ct)) return Error(409, "slug_conflict");
        var now = clock.GetUtcNow().UtcDateTime;
        if (kind == "members" && await db.Products.CountAsync(p => ids!.Contains(p.Id), ct) != ids!.Count) return Error(404, "product_not_found");
        var visible = CollectionScope.VisibleProducts(db, now);
        if (kind == "members" && c.Status == "Published" && !await visible.AnyAsync(p => ids!.Contains(p.Id), ct) ||
            kind == "update" && status == "Published" && !await visible.AnyAsync(p => db.CollectionProducts.Any(l => l.CollectionId == c.Id && l.ProductId == p.Id), ct)) return Error(409, "collection_has_no_public_products");
        var additions = new List<object>();
        try
        {
            if (kind == "create")
            {
                c.CreatedAt = now; c.UpdatedAt = now;
                additions.Add(c); db.Collections.Add(c);
                await db.SaveChangesAsync(ct);
            }
            else if (kind == "update")
                await db.Collections.Where(x => x.Id == c.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DisplayName, name!)
                    .SetProperty(x => x.Slug, slug!).SetProperty(x => x.Status, status!).SetProperty(x => x.UpdatedAt, now), ct);
            else
            {
                var existing = await db.CollectionProducts.Where(l => l.CollectionId == c.Id).Select(l => l.ProductId).ToArrayAsync(ct);
                var desired = ids!.ToArray();
                if (existing.Except(desired).Any())
                    await db.CollectionProducts.Where(l => l.CollectionId == c.Id && !desired.Contains(l.ProductId)).ExecuteDeleteAsync(ct);
                foreach (var product in desired.Except(existing)) additions.Add(new CollectionProduct { CollectionId = c.Id, ProductId = product, CreatedAt = now });
                db.AddRange(additions);
                await db.SaveChangesAsync(ct);
            }
            var current = await Project(db.Collections.AsNoTracking().Where(x => x.Id == c.Id), true).SingleAsync(ct);
            var body = new CollectionCommandResponse(false, current);
            var operation = new CollectionOperation { CollectionId = c.Id, ActorUserId = actor, OperationKey = key,
                Kind = kind, RequestHash = hash, ResponseJson = JsonSerializer.Serialize(body, Json), StatusCode = kind == "create" ? 201 : 200, CreatedAt = now };
            additions.Add(operation); db.CollectionOperations.Add(operation);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(operation.StatusCode, body);
        }
        finally { foreach (var item in additions) db.Entry(item).State = EntityState.Detached; }
    }
}
