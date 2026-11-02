using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class DynamicPromotionModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<Promotion>(b =>
        {
            b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.CategoryId).HasColumnName("category_id");
            b.HasIndex(x => new { x.Scope, x.Status });
            b.ToTable("promotions", "app", t => t.HasCheckConstraint("CK_promotions_scope",
                "(scope='product' AND product_id IS NOT NULL AND collection_id IS NULL AND category_id IS NULL) OR " +
                "(scope='collection' AND product_id IS NULL AND collection_id IS NOT NULL AND category_id IS NULL) OR " +
                "(scope='catalog' AND product_id IS NULL AND collection_id IS NULL AND category_id IS NULL) OR " +
                "(scope='category' AND product_id IS NULL AND collection_id IS NULL AND category_id IS NOT NULL)"));
        });
        model.Entity<PromotionVariant>().HasIndex(x => new { x.PromotionId, x.VariantId }).IsUnique(false);
        model.Entity<PromotionOperation>().ToTable("promotion_operations", "app", t => t.HasCheckConstraint("CK_promotion_operations_actor",
            "(actor_user_id IS NULL AND operation_key IS NULL AND kind IN ('demo','dynamic_reconcile')) OR (actor_user_id IS NOT NULL AND operation_key IS NOT NULL)"));
        model.Entity<PromotionExclusionRule>(b =>
        {
            b.ToTable("promotion_exclusions", "app", t => t.HasCheckConstraint("CK_promotion_exclusions_target", "(product_id IS NULL) <> (variant_id IS NULL)"));
            b.HasKey(x => x.Id);
            b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.PromotionId, x.ProductId }).IsUnique().HasFilter("product_id IS NOT NULL");
            b.HasIndex(x => new { x.PromotionId, x.VariantId }).IsUnique().HasFilter("variant_id IS NOT NULL");
        });
        model.Entity<PromotionConflict>(b =>
        {
            b.ToTable("promotion_conflicts", "app");
            b.HasKey(x => x.Id);
            b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.OtherPromotionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Code).HasMaxLength(80);
            b.HasIndex(x => new { x.PromotionId, x.VariantId, x.Code }).IsUnique().HasFilter("resolved_at IS NULL");
            b.HasIndex(x => new { x.ResolvedAt, x.UpdatedAt });
        });
        model.Entity<PromotionReconciliation>(b =>
        {
            b.ToTable("promotion_reconciliation", "app", t => t.HasCheckConstraint("CK_promotion_reconciliation_singleton", "id=1"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });
        foreach (var type in new[] { typeof(PromotionExclusionRule), typeof(PromotionConflict), typeof(PromotionReconciliation) })
            foreach (var p in model.Entity(type).Metadata.GetProperties())
                p.SetColumnName(string.Concat(p.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
