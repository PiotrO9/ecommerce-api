namespace E_commerce_backend.Contracts.Catalog;

public sealed record ProductCategoryListResponse(
    IReadOnlyList<ProductDetailsResponse> Items,
    ProductListPaginationResponse? Pagination);

public sealed record ProductListPaginationResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);
