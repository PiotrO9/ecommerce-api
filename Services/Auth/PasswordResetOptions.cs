namespace E_commerce_backend.Services.Auth;

public sealed record PasswordResetOptions(Uri Url, string FromEmail, string FromName);
