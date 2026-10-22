namespace E_commerce_backend.Entities;

public sealed class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public required string SKU { get; set; }
    public CatalogStatus Status { get; set; } = CatalogStatus.Draft;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string[] ImageUrls { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public VariantStock? Stock { get; set; }
    public ICollection<VariantPriceChange> PriceChanges { get; set; } = new List<VariantPriceChange>();
    public ICollection<VariantAttributePair> VariantAttributePairs { get; set; } = new List<VariantAttributePair>();
}
