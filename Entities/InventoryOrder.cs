namespace E_commerce_backend.Entities;

// Minimal purchase snapshot used by inventory. Delivery and payment providers are separate modules.
public sealed class InventoryOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid OperationKey { get; set; }
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "PendingPayment";
    public decimal GoodsTotal { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class InventoryOrderItem
{
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string AttributesJson { get; set; } = "[]";
    public Guid PriceChangeId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class StockReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class StockReservationItem
{
    public Guid ReservationId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
}

public sealed class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReservationId { get; set; }
    public Guid VariantId { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Only a trusted server-side payment adapter can call the settlement service.
public sealed class InventoryPaymentReceipt
{
    public Guid EventId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Outcome { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
