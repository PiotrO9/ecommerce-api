using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed class AttributeManagementService(AppDbContext db, CatalogCommandStore store, TimeProvider clock)
{
    public Task<CatalogResult> CreateKeyAsync(KeyCreateRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var code = AttributeKey.NormalizeCode(CatalogInput.Text(r.Code, 100, "code")!);
        var name = CatalogInput.Text(r.DisplayName, 120, "displayName")!;
        return store.ExecuteAsync("key.create", Guid.Empty, null, new { code, name }, actor, key, async () =>
        {
            var existing = await db.AttributeKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Code == code, ct);
            if (existing is not null)
            {
                if (existing.DisplayName != name) throw new CatalogValidation("attribute_key_label_conflict", "displayName", 409);
                return new(new(false, existing.Id, existing.Version));
            }
            var item = new AttributeKey { Code = code, DisplayName = name };
            db.AttributeKeys.Add(item); await db.SaveChangesAsync(ct);
            return new(new(false, item.Id, item.Version), 201);
        }, ct);
    }
    public Task<CatalogResult> EditKeyAsync(Guid id, KeyEditRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var name = CatalogInput.Text(r.DisplayName, 120, "displayName")!;
        return store.ExecuteAsync("key.update", id, r.ExpectedVersion, name, actor, key, async () =>
        {
            var item = CatalogCommandStore.Found(await db.AttributeKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == id, ct), "attribute_key");
            CatalogCommandStore.Version(item.Version, r.ExpectedVersion);
            await db.AttributeKeys.Where(k => k.Id == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.DisplayName, name), ct);
            return new(new(false, id, await db.AttributeKeys.Where(k => k.Id == id).Select(k => k.Version).SingleAsync(ct)));
        }, ct);
    }
    public Task<CatalogResult> CreatePairAsync(Guid id, PairCreateRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var value = CatalogInput.Text(r.Value, 255, "value")!;
        return store.ExecuteAsync("pair.create", id, null, value, actor, key, async () =>
        {
            if (!await db.AttributeKeys.AnyAsync(k => k.Id == id, ct)) throw new CatalogValidation("attribute_key_not_found", status: 404);
            var existing = await db.AttributePairs.AsNoTracking().SingleOrDefaultAsync(p => p.AttributeKeyId == id && p.Value == value, ct);
            if (existing is not null) return new(new(false, existing.Id, null));
            var now = clock.GetUtcNow().UtcDateTime;
            var pair = new AttributePair { AttributeKeyId = id, Value = value, CreatedAt = now, UpdatedAt = now };
            db.AttributePairs.Add(pair); await db.SaveChangesAsync(ct);
            return new(new(false, pair.Id, null), 201);
        }, ct);
    }
}
