namespace E_commerce_backend.Contracts.Catalog;

public sealed class ProductSearchRequest
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategorySlug { get; set; }
    public List<ProductAttributeFilterRequest>? Attributes { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? Sort { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
}
