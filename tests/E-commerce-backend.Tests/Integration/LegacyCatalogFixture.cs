using E_commerce_backend.Data;
using E_commerce_backend.Services.Pricing;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Integration;

internal static class LegacyCatalogFixture
{
    // Privileged test fixture only: preserve read coverage of pre-publication-rule
    // imports (foreign/future-only prices). This is never an application bypass.
    public static async Task ImportAsync(AppDbContext db, Func<Task> import)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await CatalogWriteLock.AcquireAsync(db, default);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE app.products DISABLE TRIGGER check_catalog_product_publication;
            ALTER TABLE app.product_variants DISABLE TRIGGER check_catalog_variant_publication;
            ALTER TABLE app.product_categories DISABLE TRIGGER check_catalog_category_publication;
            """);
        await import();
        // Flush the original deferred graph guards before ALTER TABLE; they
        // still validate the fixture while the new publication guards are off.
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        // Flush deferred events before ALTER TABLE re-enables the import guards.
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE app.products ENABLE TRIGGER check_catalog_product_publication;
            ALTER TABLE app.product_variants ENABLE TRIGGER check_catalog_variant_publication;
            ALTER TABLE app.product_categories ENABLE TRIGGER check_catalog_category_publication;
            """);
        await tx.CommitAsync();
    }
}
