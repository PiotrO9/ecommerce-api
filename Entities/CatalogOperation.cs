namespace E_commerce_backend.Entities;

// A retained audit reference, deliberately without a target/user FK: deleting an
// empty category or revoking administrator access must not delete replay data.
public sealed class CatalogOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Kind { get; set; }
    public Guid TargetId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid OperationKey { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseJson { get; set; }
    public long? ExpectedVersion { get; set; }
    public int StatusCode { get; set; }
    public string? Reason { get; set; }
    public int? QuantityBefore { get; set; }
    public int? QuantityAfter { get; set; }
    public DateTime CreatedAt { get; set; }
}
