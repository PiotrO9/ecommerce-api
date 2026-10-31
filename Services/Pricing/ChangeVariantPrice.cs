namespace E_commerce_backend.Services.Pricing;

// ActorUserId must come from a trusted caller after authorization, never from an HTTP body.
public sealed record ChangeVariantPrice(
    Guid VariantId,
    decimal BaseAmount,
    string? Currency,
    string? Reason,
    Guid? ExpectedPriceChangeId,
    Guid OperationKey,
    Guid ActorUserId);

public enum PriceChangeStatus
{
    Applied,
    Replayed,
    InvalidRequest,
    VariantNotFound,
    VersionConflict,
    OperationKeyConflict,
    DiscountedPriceConflict,
    FuturePriceConflict,
    TimestampConflict
}

public sealed record PriceChangeReceipt(Guid Id, Guid VariantId, decimal BaseAmount,
    decimal SellingAmount, string Currency, DateTime EffectiveAt, DateTime CreatedAt,
    string Reason, Guid? PreviousPriceChangeId);

public sealed record PriceChangeResult(PriceChangeStatus Status, PriceChangeReceipt? Price = null);
