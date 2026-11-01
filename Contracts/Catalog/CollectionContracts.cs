using System.Text.Json.Serialization;

namespace E_commerce_backend.Contracts.Catalog;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CollectionWriteRequest
{
    public string? DisplayName { get; set; }
    public string? Slug { get; set; }
    public string? Status { get; set; }
    public long? ExpectedVersion { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CollectionProductsRequest
{
    public long? ExpectedVersion { get; set; }
    public List<Guid>? ProductIds { get; set; }
}
public sealed record CollectionResponse(Guid Id, string DisplayName, string Slug, string Status,
    long Version, DateTime CreatedAt, DateTime UpdatedAt, int ProductCount);
public sealed record CollectionListResponse(IReadOnlyList<CollectionResponse> Items, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record CollectionProductResponse(Guid Id, string Name, string Slug, string Status);
public sealed record CollectionProductsResponse(long CollectionVersion, IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<CollectionProductResponse> Items, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record CollectionCommandResponse(bool Replayed, CollectionResponse Collection);
public sealed record CollectionError(string Code, string Message);
public sealed record CollectionResult(int Status, object Body);
