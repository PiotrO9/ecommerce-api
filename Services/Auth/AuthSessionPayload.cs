namespace E_commerce_backend.Services.Auth;

public sealed record AuthSessionPayload(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AbsoluteExpiresAt);
