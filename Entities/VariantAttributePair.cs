namespace E_commerce_backend.Entities;

public sealed class VariantAttributePair
{
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public Guid AttributePairId { get; set; }
    public AttributePair AttributePair { get; set; } = null!;
}
