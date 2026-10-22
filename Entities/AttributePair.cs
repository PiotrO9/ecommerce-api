namespace E_commerce_backend.Entities;

public sealed class AttributePair
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttributeKeyId { get; set; }
    public AttributeKey AttributeKey { get; set; } = null!;
    public required string Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<VariantAttributePair> VariantAttributePairs { get; set; } = new List<VariantAttributePair>();
}
