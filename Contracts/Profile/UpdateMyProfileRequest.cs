using System.ComponentModel.DataAnnotations;

namespace E_commerce_backend.Contracts.Profile;

public sealed class UpdateMyProfileRequest
{
    [Required]
    [StringLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; init; } = string.Empty;
}
