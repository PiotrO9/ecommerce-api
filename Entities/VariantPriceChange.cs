namespace E_commerce_backend.Entities;

public sealed class VariantPriceChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;
    public decimal BaseAmount { get; set; }
    public decimal SellingAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateTime EffectiveAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string Reason { get; set; }
    public Guid? OperationKey { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public Guid? PreviousPriceChangeId { get; set; }
}
