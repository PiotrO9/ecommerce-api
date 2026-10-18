using System.ComponentModel.DataAnnotations;

namespace E_commerce_backend.Contracts.Auth;

public sealed class ConfirmPasswordResetRequest
{
    [Required]
    public string TokenHash { get; init; } = string.Empty;

    [Required, MinLength(6), MaxLength(50)]
    public string NewPassword { get; init; } = string.Empty;

    [Required, Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; init; } = string.Empty;
}
