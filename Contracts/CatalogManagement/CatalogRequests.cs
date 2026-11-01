using System.Text.Json.Serialization;

namespace E_commerce_backend.Contracts.CatalogManagement;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductCreateRequest(string? Name, string? Slug, Guid[]? CategoryIds, VariantDraftRequest? FirstVariant);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductEditRequest(string? Name, string? Slug, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VariantDraftRequest(string? Sku, string? Description, string? ShortDescription, string[]? ImageUrls, Guid[]? AttributePairIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VariantCreateRequest(long ExpectedProductVersion, string? Sku, string? Description, string? ShortDescription, string[]? ImageUrls, Guid[]? AttributePairIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VariantEditRequest(string? Sku, string? Description, string? ShortDescription, string[]? ImageUrls, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CategoryIdsRequest(Guid[]? CategoryIds, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttributeIdsRequest(Guid[]? AttributePairIds, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatusRequest(string? Status, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CategoryCreateRequest(string? DisplayName, string? Slug, Guid? ParentId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CategoryEditRequest(string? DisplayName, string? Slug, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CategoryParentRequest(Guid? ParentId, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KeyCreateRequest(string? Code, string? DisplayName);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record KeyEditRequest(string? DisplayName, long ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PairCreateRequest(string? Value);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockSetRequest([property: JsonRequired] int QuantityOnHand, [property: JsonRequired] long? ExpectedVersion, string? Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminProductSearchRequest(string? Q = null, string? Status = null, Guid? CategoryId = null,
    Guid? CollectionId = null, bool? MissingPrice = null, bool? MissingStock = null, int Page = 1, int PageSize = 20);
