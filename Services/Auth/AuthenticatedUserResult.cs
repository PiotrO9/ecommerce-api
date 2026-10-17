namespace E_commerce_backend.Services.Auth;

public enum AuthenticatedUserStatus
{
    Success,
    Unauthenticated,
    Unavailable
}

public sealed record AuthenticatedUserResult(
    AuthenticatedUserStatus Status,
    Guid? UserId = null,
    string? Email = null);
