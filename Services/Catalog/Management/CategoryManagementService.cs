using E_commerce_backend.Contracts.CatalogManagement;
using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Catalog.Management;

public sealed class CategoryManagementService(AppDbContext db, CatalogCommandStore store, TimeProvider clock)
{
    public Task<CatalogResult> CreateAsync(CategoryCreateRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var name = CatalogInput.Text(r.DisplayName, 120, "displayName")!; var slug = CatalogInput.Slug(r.Slug, name);
        return store.ExecuteAsync("category.create", Guid.Empty, null, new { name, slug, r.ParentId }, actor, key, async () =>
        {
            await Parent(r.ParentId, null, ct); await Slug(slug, r.ParentId, null, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var c = new Category { DisplayName = name, Slug = slug, ParentId = r.ParentId, CreatedAt = now, UpdatedAt = now };
            db.Categories.Add(c); await db.SaveChangesAsync(ct);
            return new(new(false, c.Id, c.Version), 201);
        }, ct);
    }
    public Task<CatalogResult> EditAsync(Guid id, CategoryEditRequest r, Guid actor, Guid key, CancellationToken ct)
    {
        var name = CatalogInput.Text(r.DisplayName, 120, "displayName")!;
        // Unlike create, a missing slug must not silently change an existing URL.
        if (r.Slug is null) throw new CatalogValidation("slug_required", "slug");
        var slug = CatalogInput.Slug(r.Slug, name);
        return store.ExecuteAsync("category.update", id, r.ExpectedVersion, new { name, slug }, actor, key, async () =>
        {
            var c = await Category(id, r.ExpectedVersion, ct); await Slug(slug, c.ParentId, id, ct);
            await db.Categories.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.DisplayName, name).SetProperty(c => c.Slug, slug), ct);
            return await Response(id, ct);
        }, ct);
    }
    public Task<CatalogResult> ParentAsync(Guid id, CategoryParentRequest r, Guid actor, Guid key, CancellationToken ct) =>
        store.ExecuteAsync("category.parent", id, r.ExpectedVersion, r.ParentId ?? Guid.Empty, actor, key, async () =>
        {
            var c = await Category(id, r.ExpectedVersion, ct); await Parent(r.ParentId, id, ct); await Slug(c.Slug, r.ParentId, id, ct);
            await db.Categories.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentId, r.ParentId), ct);
            return await Response(id, ct);
        }, ct);
    public Task<CatalogResult> DeleteAsync(Guid id, long version, Guid actor, Guid key, CancellationToken ct) =>
        store.ExecuteAsync("category.delete", id, version, new { }, actor, key, async () =>
        {
            await Category(id, version, ct);
            if (await db.Categories.AnyAsync(c => c.ParentId == id, ct) || await db.ProductCategories.AnyAsync(c => c.CategoryId == id, ct))
                throw new CatalogValidation("category_in_use", status: 409);
            await db.Categories.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
            return new(new(false, id, version), 204);
        }, ct);
    private async Task<Category> Category(Guid id, long version, CancellationToken ct)
    {
        var c = CatalogCommandStore.Found(await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct), "category");
        CatalogCommandStore.Version(c.Version, version); return c;
    }
    private async Task Parent(Guid? id, Guid? target, CancellationToken ct)
    {
        if (id is null) return;
        var rows = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToDictionaryAsync(c => c.Id, ct);
        if (!rows.ContainsKey(id.Value)) throw new CatalogValidation("parent_category_not_found", status: 404);
        var visited = new HashSet<Guid>();
        while (id is Guid current)
        {
            if (current == target || !visited.Add(current)) throw new CatalogValidation("category_cycle", "parentId", 409);
            id = rows[current].ParentId;
        }
    }
    private async Task Slug(string slug, Guid? parent, Guid? id, CancellationToken ct)
    { if (await db.Categories.AnyAsync(c => c.Slug == slug && c.ParentId == parent && c.Id != id, ct)) throw new CatalogValidation("slug_conflict", "slug", 409); }
    private async Task<CatalogChange> Response(Guid id, CancellationToken ct) =>
        new(new(false, id, await db.Categories.Where(c => c.Id == id).Select(c => c.Version).SingleAsync(ct)));
}
