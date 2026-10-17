namespace E_commerce_backend.Services.Auth;

public enum SessionRefreshStatus
{
    Success,
    Invalid,
    Unavailable
}

public sealed record SessionRefreshResult(
    SessionRefreshStatus Status,
    AuthSessionPayload? Session = null);
