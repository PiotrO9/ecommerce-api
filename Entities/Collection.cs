namespace E_commerce_backend.Entities;

public sealed class Collection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string DisplayName { get; set; }
    public required string Slug { get; set; }
    public string Status { get; set; } = "Draft";
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CollectionProduct
{
    public Guid CollectionId { get; set; }
    public Guid ProductId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CollectionOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CollectionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid OperationKey { get; set; }
    public required string Kind { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseJson { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
