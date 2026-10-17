namespace E_commerce_backend.Entities;

public sealed class UserProfile
{
    public Guid UserId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}
