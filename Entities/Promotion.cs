namespace E_commerce_backend.Entities;

public sealed class Promotion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProductId { get; set; }
    public Guid? CollectionId { get; set; }
    public string Scope { get; set; } = "product";
    public required string Name { get; set; }
    public required string DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public string Status { get; set; } = "Draft";
    public long Version { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public sealed class PromotionVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromotionId { get; set; }
    public Guid VariantId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public sealed class PriceReduction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromotionVariantId { get; set; }
    public Guid VariantId { get; set; }
    public decimal PriorAmount { get; set; }
    public DateTime ReferenceStart { get; set; }
    public DateTime StartedAt { get; set; }
    public required string Basis { get; set; }
}

public sealed class VariantOfferEvent
{
    public long Id { get; set; }
    public Guid VariantId { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool Visible { get; set; }
    public bool CompleteFromBirth { get; set; }
    public string Source { get; set; } = "database";
}

public sealed class PromotionOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; set; }
    public Guid? OperationKey { get; set; }
    public Guid PromotionId { get; set; }
    public required string Kind { get; set; }
    public required string RequestHash { get; set; }
    public required string ResponseJson { get; set; }
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
