using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class CatalogManagementConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        foreach (var type in new[] { typeof(Product), typeof(ProductVariant), typeof(Category), typeof(AttributeKey), typeof(VariantStock) })
        {
            model.Entity(type).Property<long>("Version").HasColumnName("version")
                .HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        }
        model.Entity<CatalogOperation>(b =>
        {
            b.ToTable("catalog_operations", "app", t =>
            {
                t.HasCheckConstraint("CK_catalog_operations_kind", "kind IN ('product.create','product.update','product.categories','product.status','variant.create','variant.update','variant.attributes','variant.status','category.create','category.update','category.parent','category.delete','key.create','key.update','pair.create','stock.set')");
                t.HasCheckConstraint("CK_catalog_operations_status", "status_code IN (200,201,204)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(40);
            b.Property(x => x.RequestHash).HasMaxLength(64);
            b.Property(x => x.ResponseJson).HasColumnType("jsonb");
            b.Property(x => x.Reason).HasMaxLength(120);
            b.HasIndex(x => new { x.ActorUserId, x.OperationKey }).IsUnique();
            foreach (var p in b.Metadata.GetProperties())
                p.SetColumnName(string.Concat(p.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
        });
    }
}
