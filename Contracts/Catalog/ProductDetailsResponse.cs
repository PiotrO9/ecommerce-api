namespace E_commerce_backend.Contracts.Catalog;

public sealed record ProductDetailsResponse(
    Guid Id,
    string Name,
    string Slug,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    IReadOnlyList<ProductCategoryResponse> Categories,
    IReadOnlyList<ProductVariantResponse> Variants);

public sealed record ProductCategoryResponse(Guid Id, string DisplayName, string Slug);

public sealed record ProductVariantResponse(
    Guid Id,
    string Sku,
    string? Description,
    string? ShortDescription,
    string[] ImageUrls,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    IReadOnlyList<ProductVariantAttributeResponse> Attributes,
    int? QuantityOnHand,
    IReadOnlyList<ProductVariantPriceResponse> CurrentPrices)
{
    public int? QuantityAvailable { get; init; } = QuantityOnHand;
}

public sealed record ProductVariantAttributeResponse(string Code, string DisplayName, string Value);

public sealed record ProductVariantPriceResponse(decimal Amount, string Currency,
    decimal? BaseAmount = null, PublicPriceReduction? Reduction = null, Guid? VariantId = null);

public sealed record PublicPriceReduction(decimal PriorAmount, DateTime ReferenceStart,
    DateTime StartedAt, string Basis, decimal SavingPercent);
