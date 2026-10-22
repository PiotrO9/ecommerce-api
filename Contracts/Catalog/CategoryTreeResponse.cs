namespace E_commerce_backend.Contracts.Catalog;

public sealed record CategoryTreeResponse(
    Guid Id,
    Guid? ParentId,
    string DisplayName,
    string Slug,
    List<CategoryTreeResponse> Children);
