using System.Text.Json.Serialization;
using E_commerce_backend.Contracts.Catalog;

namespace E_commerce_backend.Contracts.Shopping;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ShoppingVersionRequest
{
    public Guid? ExpectedScopeId { get; set; }
    public long? ExpectedVersion { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CartItemRequest : ShoppingVersionRequest
{
    public int? Quantity { get; set; }
}

public sealed record ShoppingError(string Code, string Message);
public sealed record ShoppingResult(int Status, object? Body = null)
{
    public static ShoppingResult Error(int status, string code, string message) => new(status, new ShoppingError(code, message));
}
public sealed record ShoppingVersionResponse(Guid ScopeId, long Version);
public sealed record GuestShoppingSessionResponse(Guid ScopeId, long Version, DateTime ExpiresAt);
public sealed record ShoppingVariantResponse(Guid VariantId, Guid ProductId, string Name, string Slug,
    string Sku, string? ThumbnailUrl, IReadOnlyList<ProductVariantAttributeResponse> Attributes,
    int? QuantityOnHand, Guid PriceChangeId, ProductVariantPriceResponse Price);
public sealed record CartItemResponse(Guid VariantId, int Quantity, string Availability,
    ShoppingVariantResponse? Variant, decimal? LineAmount);
public sealed record CartResponse(Guid? ScopeId, long Version, DateTime EvaluatedAt, string Currency,
    IReadOnlyList<CartItemResponse> Items, decimal GoodsSubtotal, int BlockedItems, bool CanCheckout);
public sealed record FavoriteItemResponse(Guid VariantId, DateTime CreatedAt, string Availability,
    ShoppingVariantResponse? Variant);
public sealed record FavoritesResponse(Guid ScopeId, long Version, DateTime EvaluatedAt,
    IReadOnlyList<FavoriteItemResponse> Items, int Page, int PageSize, int TotalItems, int TotalPages);
