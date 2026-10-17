using E_commerce_backend.Data;
using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Profile;

public sealed class UserProfileReader(AppDbContext database) : IUserProfileReader
{
    public Task<UserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        database.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
}
