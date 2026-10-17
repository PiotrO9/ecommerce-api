using E_commerce_backend.Entities;
using E_commerce_backend.Services.Profile;

namespace E_commerce_backend.Tests.Unit;

internal sealed class StubUserProfileReader : IUserProfileReader
{
    public UserProfile? Profile { get; set; }
    public Guid? RequestedUserId { get; private set; }

    public Task<UserProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequestedUserId = userId;
        return Task.FromResult(Profile?.UserId == userId ? Profile : null);
    }
}
