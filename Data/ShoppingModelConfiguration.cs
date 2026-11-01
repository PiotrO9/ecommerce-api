using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class ShoppingModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ShoppingState>(b =>
        {
            b.ToTable("shopping_states", "app", t =>
            {
                t.HasCheckConstraint("CK_shopping_states_owner", "(user_id IS NOT NULL AND guest_token_hash IS NULL AND expires_at IS NULL) OR (user_id IS NULL AND guest_token_hash IS NOT NULL AND guest_token_hash ~ '^[0-9A-F]{64}$' AND expires_at IS NOT NULL)");
                t.HasCheckConstraint("CK_shopping_states_version", "cart_version >= 0");
            });
            b.HasKey(x => x.Id);
            b.HasOne<AuthUserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Property(x => x.GuestTokenHash).HasMaxLength(64);
            b.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
            b.HasIndex(x => x.GuestTokenHash).IsUnique().HasFilter("guest_token_hash IS NOT NULL");
            b.HasIndex(x => x.ExpiresAt).HasFilter("expires_at IS NOT NULL");
            b.Property(x => x.CartVersion).IsConcurrencyToken();
        });
        model.Entity<CartItem>(b =>
        {
            b.ToTable("cart_items", "app", t => t.HasCheckConstraint("CK_cart_items_quantity", "quantity BETWEEN 1 AND 99"));
            b.HasKey(x => new { x.ShoppingStateId, x.VariantId });
            b.HasOne<ShoppingState>().WithMany().HasForeignKey(x => x.ShoppingStateId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<FavoriteList>(b =>
        {
            b.ToTable("favorite_lists", "app", t => t.HasCheckConstraint("CK_favorite_lists_version", "version >= 0"));
            b.HasKey(x => x.UserId);
            b.HasOne<AuthUserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ScopeId).IsUnique();
            b.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<FavoriteItem>(b =>
        {
            b.ToTable("favorite_items", "app");
            b.HasKey(x => new { x.UserId, x.VariantId });
            b.HasOne<FavoriteList>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.CreatedAt, x.VariantId });
        });
        foreach (var type in new[] { typeof(ShoppingState), typeof(CartItem), typeof(FavoriteList), typeof(FavoriteItem) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
