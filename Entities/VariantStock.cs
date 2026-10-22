namespace E_commerce_backend.Entities;

public sealed class VariantStock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public int QuantityOnHand { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
