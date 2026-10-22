namespace E_commerce_backend.Contracts.Catalog;

public sealed record CategoryDetailsResponse(
    Guid Id,
    string DisplayName,
    string Slug,
    Guid? ParentId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
