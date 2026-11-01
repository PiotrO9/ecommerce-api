using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class CollectionModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Collection>(b =>
        {
            b.ToTable("collections", "app", t =>
            {
                t.HasCheckConstraint("CK_collections_name", "length(btrim(display_name)) BETWEEN 1 AND 120");
                t.HasCheckConstraint("CK_collections_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                t.HasCheckConstraint("CK_collections_state", "status IN ('Draft','Published','Hidden') AND version > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.DisplayName).HasMaxLength(120);
            b.Property(x => x.Slug).HasMaxLength(160);
            b.Property(x => x.Version).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
            b.HasIndex(x => x.Slug).IsUnique();
        });
        model.Entity<CollectionProduct>(b =>
        {
            b.ToTable("collection_products", "app");
            b.HasKey(x => new { x.CollectionId, x.ProductId });
            b.HasOne<Collection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ProductId);
        });
        model.Entity<CollectionOperation>(b =>
        {
            b.ToTable("collection_operations", "app");
            b.HasKey(x => x.Id);
            b.HasOne<Collection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.ActorUserId, x.OperationKey }).IsUnique();
            b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        });
        foreach (var type in new[] { typeof(Collection), typeof(CollectionProduct), typeof(CollectionOperation) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
        model.Entity<Promotion>(b =>
        {
            b.Property(x => x.Scope).HasColumnName("scope").HasDefaultValue("product");
            b.Property(x => x.CollectionId).HasColumnName("collection_id");
            b.HasOne<Collection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("promotions", "app", t => t.HasCheckConstraint("CK_promotions_scope",
                "(scope = 'product' AND product_id IS NOT NULL AND collection_id IS NULL) OR (scope = 'collection' AND product_id IS NULL AND collection_id IS NOT NULL)"));
        });
    }
}
