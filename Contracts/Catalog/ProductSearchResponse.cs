namespace E_commerce_backend.Contracts.Catalog;

public sealed record ProductSearchResponse(
    IReadOnlyList<ProductListItemResponse> Items,
    ProductListPaginationResponse? Pagination,
    IReadOnlyList<ProductSearchFilterResponse> AvailableFilters);

public sealed record ProductSearchFilterResponse(
    string Code,
    string DisplayName,
    IReadOnlyList<ProductSearchFilterValueResponse> Values);

public sealed record ProductSearchFilterValueResponse(string Value, int Count, bool Selected);
