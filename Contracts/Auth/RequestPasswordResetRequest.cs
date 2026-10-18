using System.ComponentModel.DataAnnotations;

namespace E_commerce_backend.Contracts.Auth;

public sealed class RequestPasswordResetRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
}
