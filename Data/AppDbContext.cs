using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

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
            profile.HasOne<AuthUserReference>()
                .WithOne()
                .HasForeignKey<UserProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
