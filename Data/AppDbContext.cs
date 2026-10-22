using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<AttributeKey> AttributeKeys => Set<AttributeKey>();
    public DbSet<AttributePair> AttributePairs => Set<AttributePair>();
    public DbSet<VariantAttributePair> VariantAttributePairs => Set<VariantAttributePair>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<VariantStock> VariantStocks => Set<VariantStock>();
    public DbSet<VariantPriceChange> VariantPriceChanges => Set<VariantPriceChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthUserReference>(user =>
        {
            user.ToTable("users", "auth", table => table.ExcludeFromMigrations());
            user.HasKey(x => x.Id);
            user.Property(x => x.Id).HasColumnName("id");
        });

        modelBuilder.Entity<UserProfile>(profile =>
        {
            profile.ToTable("user_profiles", "app");
            profile.HasKey(x => x.UserId);
            profile.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            profile.Property(x => x.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            profile.Property(x => x.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
            profile.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            profile.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
            profile.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasColumnType("text").IsRequired(false);
            profile.HasOne<AuthUserReference>()
                .WithOne()
                .HasForeignKey<UserProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        CatalogModelConfiguration.Configure(modelBuilder);
    }
}
