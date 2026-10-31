using System.Text.Json.Serialization;

namespace E_commerce_backend.Contracts.Pricing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeVariantPriceRequest
{
    public decimal? BaseAmount { get; set; }
    public string? Currency { get; set; }
    public string? Reason { get; set; }
    public Guid? ExpectedPriceChangeId { get; set; }
}

public sealed record AdminPriceChangeResponse(Guid Id, Guid VariantId, decimal BaseAmount,
    decimal SellingAmount, string Currency, DateTime EffectiveAt, DateTime CreatedAt,
    string Reason, Guid? PreviousPriceChangeId, Guid? ChangedByUserId);

public sealed record CurrentVariantPriceResponse(AdminPriceChangeResponse? Price);

public sealed record ChangeVariantPriceResponse(bool Replayed, AdminPriceChangeResponse Price);

public sealed record PriceHistoryResponse(IReadOnlyList<AdminPriceChangeResponse> Items,
    int Page, int PageSize, int TotalItems, int TotalPages);
