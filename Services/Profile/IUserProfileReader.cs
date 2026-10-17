using E_commerce_backend.Entities;

namespace E_commerce_backend.Services.Profile;

public interface IUserProfileReader
{
    Task<UserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
