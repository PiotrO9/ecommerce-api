using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class PromotionModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Promotion>(b =>
        {
            b.ToTable("promotions", "app", t =>
            {
                t.HasCheckConstraint("CK_promotions_rule", "discount_type IN ('percentage','amount') AND discount_value > 0 AND (discount_type <> 'percentage' OR discount_value <= 100)");
                t.HasCheckConstraint("CK_promotions_state", "status IN ('Draft','Active','Ended') AND version > 0 AND ((status = 'Draft' AND activated_at IS NULL AND ended_at IS NULL) OR (status = 'Active' AND activated_at IS NOT NULL AND ended_at IS NULL) OR (status = 'Ended' AND activated_at IS NOT NULL AND ended_at IS NOT NULL AND ended_at >= activated_at))");
                t.HasCheckConstraint("CK_promotions_name", "length(btrim(name)) BETWEEN 1 AND 120");
            });
            b.HasKey(x => x.Id);
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Name).HasMaxLength(120);
            b.Property(x => x.DiscountValue).HasPrecision(18, 2);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.ProductId, x.Status });
        });
        model.Entity<PromotionVariant>(b =>
        {
            b.ToTable("promotion_variants", "app", t => t.HasCheckConstraint("CK_promotion_variants_dates", "ended_at IS NULL OR ended_at >= started_at"));
            b.HasKey(x => x.Id);
            b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.VariantId).IsUnique().HasFilter("ended_at IS NULL");
            b.HasIndex(x => new { x.PromotionId, x.VariantId }).IsUnique();
        });
        model.Entity<PriceReduction>(b =>
        {
            b.ToTable("price_reductions", "app", t => t.HasCheckConstraint("CK_price_reductions_reference", "prior_amount >= 0 AND reference_start < started_at AND basis IN ('Full30Days','SinceFirstOffer')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.PriorAmount).HasPrecision(18, 2);
            b.HasOne<PromotionVariant>().WithOne().HasForeignKey<PriceReduction>(x => x.PromotionVariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<VariantOfferEvent>(b =>
        {
            b.ToTable("variant_offer_events", "app", t => t.HasCheckConstraint("CK_variant_offer_events_source", "source IN ('database','import','demo')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.VariantId, x.OccurredAt, x.Id });
        });
        model.Entity<PromotionOperation>(b =>
        {
            b.ToTable("promotion_operations", "app", t => t.HasCheckConstraint("CK_promotion_operations_actor", "(actor_user_id IS NULL AND operation_key IS NULL AND kind = 'demo') OR (actor_user_id IS NOT NULL AND operation_key IS NOT NULL)"));
            b.HasKey(x => x.Id);
            b.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.ActorUserId, x.OperationKey }).IsUnique().HasFilter("operation_key IS NOT NULL");
            b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        });
        // Explicit snake_case columns; project has no global naming convention.
        foreach (var type in new[] { typeof(Promotion), typeof(PromotionVariant), typeof(PriceReduction), typeof(VariantOfferEvent), typeof(PromotionOperation) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));

        model.Entity<VariantPriceChange>(b =>
        {
            b.Property(x => x.PromotionOperationId).HasColumnName("promotion_operation_id");
            b.Property(x => x.PriceReductionId).HasColumnName("price_reduction_id");
            b.HasOne<PromotionOperation>().WithMany().HasForeignKey(x => x.PromotionOperationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.PriceReduction).WithMany().HasForeignKey(x => x.PriceReductionId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("variant_price_changes", "app", t => t.HasCheckConstraint("CK_variant_price_changes_operation",
                "(promotion_operation_id IS NULL AND price_reduction_id IS NULL AND ((operation_key IS NULL AND changed_by_user_id IS NULL AND previous_price_change_id IS NULL) OR (operation_key IS NOT NULL AND changed_by_user_id IS NOT NULL))) OR (promotion_operation_id IS NOT NULL AND operation_key IS NULL AND changed_by_user_id IS NULL)"));
        });
    }
}
