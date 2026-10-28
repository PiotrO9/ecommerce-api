namespace E_commerce_backend.Contracts.Catalog;

public sealed record ProductListResponse(
    IReadOnlyList<ProductListItemResponse> Items,
    ProductListPaginationResponse? Pagination);

public sealed record ProductListItemResponse(
    Guid Id,
    string Name,
    string Slug,
    string? ThumbnailUrl,
    IReadOnlyList<ProductVariantPriceResponse> PricesFrom);

public sealed record ProductListPaginationResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
