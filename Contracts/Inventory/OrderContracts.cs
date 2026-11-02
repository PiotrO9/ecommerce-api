using System.Text.Json.Serialization;

namespace E_commerce_backend.Contracts.Inventory;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderCreateRequest(Guid ExpectedScopeId, long ExpectedVersion, IReadOnlyList<OrderPriceAcceptance>? Items);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderPriceAcceptance(Guid VariantId, int Quantity, Guid PriceChangeId, decimal UnitPrice);

public sealed record OrderItemResponse(Guid VariantId, string Name, string Sku, int Quantity, decimal UnitPrice);
public sealed record OrderResponse(Guid Id, string Status, DateTime CreatedAt, DateTime ExpiresAt,
    string ReservationStatus, decimal GoodsTotal, string Currency, IReadOnlyList<OrderItemResponse> Items, bool Replayed = false);
public sealed record OrderPage(IReadOnlyList<OrderResponse> Items, int Page, int PageSize, int TotalItems);

public sealed class InventoryOptions
{
    public int ReservationMinutes { get; set; } = 15;
    public int ExpiryIntervalSeconds { get; set; } = 30;
}
