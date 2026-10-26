namespace E_commerce_backend.Contracts.Catalog;

public sealed class ProductAttributeFilterRequest
{
    public string? Code { get; set; }
    public List<string>? Values { get; set; }
}
