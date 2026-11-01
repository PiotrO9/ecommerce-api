using System.Text.Json.Serialization;

namespace E_commerce_backend.Contracts.Pricing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PromotionDraftRequest
{
    public Guid? ProductId { get; set; }
    public Guid? CollectionId { get; set; }
    public string? Scope { get; set; }
    public string? Name { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public long? ExpectedVersion { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class PromotionVersionRequest
{
    public long? ExpectedVersion { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PromotionActivationRequest : PromotionVersionRequest
{
    public List<ExpectedVariantPrice>? Variants { get; set; }
    public long? ExpectedCollectionVersion { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpectedVariantPrice(Guid VariantId, Guid ExpectedPriceChangeId);

public sealed record PromotionMemberResponse(Guid VariantId, DateTime StartedAt, DateTime? EndedAt,
    decimal? PriorAmount, DateTime? ReferenceStart);
public sealed record PromotionResponse(Guid Id, Guid? ProductId, string Name, string DiscountType,
    decimal DiscountValue, string Currency, string Status, long Version, DateTime CreatedAt,
    DateTime UpdatedAt, DateTime? ActivatedAt, DateTime? EndedAt, IReadOnlyList<PromotionMemberResponse> Variants,
    string Scope = "product", Guid? CollectionId = null);
public sealed record PromotionCommandResponse(bool Replayed, PromotionResponse Promotion);
public sealed record PromotionPreviewVariant(Guid VariantId, string Sku, Guid? ExpectedPriceChangeId,
    decimal? BaseAmount, decimal? SellingAmount, decimal? PriorAmount, DateTime? ReferenceStart,
    string? ReferenceBasis, IReadOnlyList<string> Errors, Guid? ProductId = null, string? ProductName = null);
public sealed record PromotionExclusion(Guid ProductId, Guid? VariantId, string Reason);
public sealed record PromotionPreviewResponse(Guid Id, long Version, bool CanActivate,
    int Page, int PageSize, int TotalVariants, IReadOnlyList<string> Errors,
    IReadOnlyList<ExpectedVariantPrice> ExpectedPrices, IReadOnlyList<PromotionPreviewVariant> Items,
    long? CollectionVersion = null, IReadOnlyList<PromotionExclusion>? Excluded = null);
public sealed record PromotionListResponse(IReadOnlyList<PromotionResponse> Items, int Page,
    int PageSize, int TotalItems, int TotalPages);
public sealed record PromotionError(string Code, string Message);
public sealed record PromotionResult(int Status, object Body);
