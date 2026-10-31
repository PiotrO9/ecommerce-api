using E_commerce_backend.Data;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Services.Auth;

public enum AdministratorAccessStatus { Allowed, Unauthenticated, Forbidden, Unavailable }

public sealed record AdministratorAccessResult(AdministratorAccessStatus Status, Guid? UserId = null);

public interface IAdministratorAccess
{
    Task<AdministratorAccessResult> CheckAsync(HttpRequest request, HttpResponse response,
        CancellationToken cancellationToken);
}

public sealed class AdministratorAccess(AuthenticatedUserService auth, AppDbContext database)
    : IAdministratorAccess
{
    public async Task<AdministratorAccessResult> CheckAsync(HttpRequest request, HttpResponse response,
        CancellationToken cancellationToken)
    {
        var user = await auth.GetAsync(request, response, cancellationToken);
        if (user.Status == AuthenticatedUserStatus.Unauthenticated)
            return new(AdministratorAccessStatus.Unauthenticated);
        if (user.Status != AuthenticatedUserStatus.Success || user.UserId is not Guid userId || userId == Guid.Empty)
            return new(AdministratorAccessStatus.Unavailable);

        // Check the database on every request: revocation does not wait for token refresh.
        var allowed = await database.Administrators.AsNoTracking()
            .AnyAsync(admin => admin.UserId == userId, cancellationToken);
        return allowed ? new(AdministratorAccessStatus.Allowed, userId)
            : new(AdministratorAccessStatus.Forbidden);
    }
}
