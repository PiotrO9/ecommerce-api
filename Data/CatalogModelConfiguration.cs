using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class CatalogModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products", "app", table =>
            {
                table.HasCheckConstraint("CK_products_name", "length(btrim(name)) > 0");
                table.HasCheckConstraint("CK_products_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                table.HasCheckConstraint("CK_products_status", "status IN ('Draft', 'Published', 'Hidden')");
            });
            product.HasKey(x => x.Id);
            product.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            product.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            product.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(160).IsRequired();
            product.HasIndex(x => x.Slug).IsUnique();
            product.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).HasDefaultValue(CatalogStatus.Draft);
            product.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            product.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            product.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<ProductVariant>(variant =>
        {
            variant.ToTable("product_variants", "app", table =>
            {
                table.HasCheckConstraint("CK_product_variants_sku", "length(btrim(sku)) > 0");
                table.HasCheckConstraint("CK_product_variants_status", "status IN ('Draft', 'Published', 'Hidden')");
            });
            variant.HasKey(x => x.Id);
            variant.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            variant.Property(x => x.ProductId).HasColumnName("product_id");
            variant.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            variant.Property(x => x.SKU).HasColumnName("sku").HasMaxLength(100).IsRequired();
            variant.HasIndex(x => x.SKU).IsUnique();
            variant.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).HasDefaultValue(CatalogStatus.Draft);
            variant.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
            variant.Property(x => x.ShortDescription).HasColumnName("short_description").HasMaxLength(500);
            variant.Property(x => x.ImageUrls).HasColumnName("image_urls").HasColumnType("text[]").IsRequired();
            variant.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            variant.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            variant.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone");
        });

        modelBuilder.Entity<AttributeKey>(key =>
        {
            key.ToTable("attribute_keys", "app", table =>
            {
                table.HasCheckConstraint("CK_attribute_keys_code", "length(code) > 0 AND code = lower(btrim(code))");
                table.HasCheckConstraint("CK_attribute_keys_display_name", "length(btrim(display_name)) > 0");
            });
            key.HasKey(x => x.Id);
            key.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            key.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
            key.HasIndex(x => x.Code).IsUnique();
            key.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<AttributePair>(pair =>
        {
            pair.ToTable("attribute_pairs", "app", table => table.HasCheckConstraint("CK_attribute_pairs_value", "length(btrim(value)) > 0"));
            pair.HasKey(x => x.Id);
            pair.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            pair.Property(x => x.AttributeKeyId).HasColumnName("attribute_key_id");
            pair.HasOne(x => x.AttributeKey).WithMany(x => x.Pairs).HasForeignKey(x => x.AttributeKeyId).OnDelete(DeleteBehavior.Restrict);
            pair.Property(x => x.Value).HasColumnName("value").HasMaxLength(255).IsRequired();
            pair.HasIndex(x => new { x.AttributeKeyId, x.Value }).IsUnique();
            pair.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            pair.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<VariantAttributePair>(link =>
        {
            link.ToTable("variant_attribute_pairs", "app");
            link.HasKey(x => new { x.VariantId, x.AttributePairId });
            link.Property(x => x.VariantId).HasColumnName("variant_id");
            link.Property(x => x.AttributePairId).HasColumnName("attribute_pair_id");
            link.HasOne(x => x.Variant).WithMany(x => x.VariantAttributePairs).HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(x => x.AttributePair).WithMany(x => x.VariantAttributePairs).HasForeignKey(x => x.AttributePairId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.ToTable("categories", "app", table =>
            {
                table.HasCheckConstraint("CK_categories_display_name", "length(btrim(display_name)) BETWEEN 1 AND 120 AND display_name = btrim(display_name)");
                table.HasCheckConstraint("CK_categories_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                table.HasCheckConstraint("CK_categories_not_own_parent", "parent_id IS NULL OR parent_id <> id");
            });
            category.HasKey(x => x.Id);
            category.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            category.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
            category.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(160).IsRequired();
            category.Property(x => x.ParentId).HasColumnName("parent_id");
            category.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            category.HasIndex(x => x.Slug).IsUnique().HasFilter("parent_id IS NULL").HasDatabaseName("UX_categories_root_slug");
            category.HasIndex(x => new { x.ParentId, x.Slug }).IsUnique().HasFilter("parent_id IS NOT NULL").HasDatabaseName("UX_categories_sibling_slug");
            category.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            category.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<ProductCategory>(link =>
        {
            link.ToTable("product_categories", "app");
            link.HasKey(x => new { x.ProductId, x.CategoryId });
            link.Property(x => x.ProductId).HasColumnName("product_id");
            link.Property(x => x.CategoryId).HasColumnName("category_id");
            link.HasOne(x => x.Product).WithMany(x => x.ProductCategories).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(x => x.Category).WithMany(x => x.ProductCategories).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VariantStock>(stock =>
        {
            stock.ToTable("variant_stock", "app", table => table.HasCheckConstraint("CK_variant_stock_nonnegative", "quantity_on_hand >= 0"));
            stock.HasKey(x => x.Id);
            stock.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            stock.Property(x => x.VariantId).HasColumnName("variant_id");
            stock.HasIndex(x => x.VariantId).IsUnique();
            stock.HasOne(x => x.Variant).WithOne(x => x.Stock).HasForeignKey<VariantStock>(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            stock.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
            stock.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            stock.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<VariantPriceChange>(price =>
        {
            price.ToTable("variant_price_changes", "app", table =>
            {
                table.HasCheckConstraint("CK_variant_price_changes_amounts", "base_amount >= 0 AND selling_amount >= 0 AND selling_amount <= base_amount");
                table.HasCheckConstraint("CK_variant_price_changes_currency", "currency ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("CK_variant_price_changes_reason", "length(btrim(reason)) > 0");
            });
            price.HasKey(x => x.Id);
            price.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            price.Property(x => x.VariantId).HasColumnName("variant_id");
            price.HasOne(x => x.Variant).WithMany(x => x.PriceChanges).HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            price.HasIndex(x => new { x.VariantId, x.Currency, x.EffectiveAt, x.CreatedAt });
            price.HasIndex(x => new { x.VariantId, x.EffectiveAt, x.CreatedAt, x.Id })
                .HasDatabaseName("IX_variant_price_changes_current_pln")
                .HasFilter("currency = 'PLN'")
                .IsDescending(false, true, true, true)
                .IncludeProperties(x => x.SellingAmount);
            price.Property(x => x.BaseAmount).HasColumnName("base_amount").HasPrecision(18, 2);
            price.Property(x => x.SellingAmount).HasColumnName("selling_amount").HasPrecision(18, 2);
            price.Property(x => x.Currency).HasColumnName("currency").HasColumnType("character(3)").HasDefaultValue("PLN").IsRequired();
            price.Property(x => x.EffectiveAt).HasColumnName("effective_at").HasColumnType("timestamp with time zone");
            price.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            price.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(120).IsRequired();
        });
    }
}
