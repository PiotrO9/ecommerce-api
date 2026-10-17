using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Tests.Unit;

public sealed class UserProfileModelTests
{
    [Fact]
    public void ProfileIsLinkedToSupabaseAuthUserByPrimaryKey()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time;Username=postgres")
            .Options;
        using var database = new AppDbContext(options);

        var profile = database.Model.FindEntityType(typeof(UserProfile));
        Assert.NotNull(profile);
        Assert.Equal("app", profile.GetSchema());
        Assert.Equal("user_profiles", profile.GetTableName());

        var primaryKey = Assert.Single(profile.GetKeys());
        Assert.Equal(nameof(UserProfile.UserId), Assert.Single(primaryKey.Properties).Name);

        var foreignKey = Assert.Single(profile.GetForeignKeys());
        Assert.Equal("auth", foreignKey.PrincipalEntityType.GetSchema());
        Assert.Equal("users", foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }
}
