namespace E_commerce_backend.Entities;

public sealed class ShoppingState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string? GuestTokenHash { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public long CartVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CartItem
{
    public Guid ShoppingStateId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class FavoriteList
{
    public Guid UserId { get; set; }
    public Guid ScopeId { get; set; } = Guid.NewGuid();
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class FavoriteItem
{
    public Guid UserId { get; set; }
    public Guid VariantId { get; set; }
    public DateTime CreatedAt { get; set; }
}
