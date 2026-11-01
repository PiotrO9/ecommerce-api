using E_commerce_backend.Contracts.Catalog;

namespace E_commerce_backend.Contracts.CatalogManagement;

public sealed record CatalogError(string Code, string Message, string? Field = null);
public sealed record CatalogResult(int Status, object? Body);
public sealed record CatalogCommandResponse(bool Replayed, Guid Id, long? Version, Guid? ProductId = null,
    long? ProductVersion = null, Guid? VariantId = null, long? VariantVersion = null);
public sealed record AdminPage<T>(IReadOnlyList<T> Items, ProductListPaginationResponse Pagination);
public sealed record AdminProductRow(Guid Id, string Name, string Slug, string Status, long Version, int VariantCount,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt);
public sealed record AdminProductDetails(AdminProductRow Product, IReadOnlyList<Guid> CategoryIds);
public sealed record AdminVariantRow(Guid Id, Guid ProductId, string Sku, string Status, long Version,
    string? Description, string? ShortDescription, IReadOnlyList<string> ImageUrls, DateTime CreatedAt,
    DateTime UpdatedAt, DateTime? PublishedAt);
public sealed record AdminVariantSummary(AdminVariantRow Variant, decimal? SellingAmount, string Currency, AdminStock? Stock);
public sealed record AdminVariantDetails(AdminVariantRow Variant, long ProductVersion, IReadOnlyList<Guid> AttributePairIds,
    decimal? SellingAmount, string Currency, AdminStock? Stock);
public sealed record AdminStock(Guid Id, Guid VariantId, int QuantityOnHand, long Version, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record AdminStockResponse(Guid VariantId, AdminStock? Stock);
public sealed record AdminCategory(Guid Id, string DisplayName, string Slug, Guid? ParentId, long Version,
    DateTime CreatedAt, DateTime UpdatedAt);
public sealed record AdminCategoryNode(AdminCategory Category, List<AdminCategoryNode> Children);
public sealed record AdminAttributeKey(Guid Id, string Code, string DisplayName, long Version);
public sealed record AdminAttributePair(Guid Id, Guid AttributeKeyId, string Value, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record ReadinessIssue(string Code, string Field, Guid? VariantId = null);
public sealed record VariantReadiness(Guid Id, long Version, bool CanPublish, IReadOnlyList<ReadinessIssue> Blockers,
    IReadOnlyList<ReadinessIssue> Warnings);
public sealed record PublicationReadiness(Guid Id, long Version, bool CanPublish, IReadOnlyList<ReadinessIssue> Blockers,
    IReadOnlyList<ReadinessIssue> Warnings, AdminPage<VariantReadiness> Variants);
