using E_commerce_backend.Entities;
using Microsoft.EntityFrameworkCore;

namespace E_commerce_backend.Data;

internal static class InventoryModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<InventoryOrder>(b =>
        {
            b.ToTable("orders", "app", t =>
            {
                t.HasCheckConstraint("CK_orders_status", "status IN ('PendingPayment','Paid','Cancelled','Expired','PaymentReview')");
                t.HasCheckConstraint("CK_orders_total", "goods_total >= 0 AND currency = 'PLN'");
            });
            b.HasKey(x => x.Id);
            b.HasOne<AuthUserReference>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.OperationKey }).IsUnique();
            b.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            b.Property(x => x.RequestHash).HasMaxLength(64);
            b.Property(x => x.Status).HasMaxLength(24);
            b.Property(x => x.GoodsTotal).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(3);
        });
        model.Entity<InventoryOrderItem>(b =>
        {
            b.ToTable("order_items", "app", t => t.HasCheckConstraint("CK_order_items_values", "quantity BETWEEN 1 AND 99 AND unit_price >= 0"));
            b.HasKey(x => new { x.OrderId, x.VariantId });
            b.HasOne<InventoryOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<VariantPriceChange>().WithMany().HasForeignKey(x => x.PriceChangeId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Sku).HasMaxLength(100);
            b.Property(x => x.AttributesJson).HasColumnType("jsonb");
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
        model.Entity<StockReservation>(b =>
        {
            b.ToTable("stock_reservations", "app", t =>
            {
                t.HasCheckConstraint("CK_stock_reservations_status", "status IN ('Active','Consumed','Released','Expired')");
                t.HasCheckConstraint("CK_stock_reservations_dates", "expires_at > created_at");
            });
            b.HasKey(x => x.Id);
            b.HasOne<InventoryOrder>().WithOne().HasForeignKey<StockReservation>(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ExpiresAt).HasFilter("status = 'Active'");
            b.Property(x => x.Status).HasMaxLength(16);
        });
        model.Entity<StockReservationItem>(b =>
        {
            b.ToTable("stock_reservation_items", "app", t => t.HasCheckConstraint("CK_stock_reservation_items_quantity", "quantity BETWEEN 1 AND 99"));
            b.HasKey(x => new { x.ReservationId, x.VariantId });
            b.HasOne<StockReservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<VariantStock>().WithMany().HasForeignKey(x => x.VariantId).HasPrincipalKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.VariantId, x.ReservationId });
        });
        model.Entity<StockMovement>(b =>
        {
            b.ToTable("stock_movements", "app", t => t.HasCheckConstraint("CK_stock_movements_quantity", "quantity_before > quantity_after AND quantity_after >= 0"));
            b.HasKey(x => x.Id);
            b.HasOne<StockReservationItem>().WithOne().HasForeignKey<StockMovement>(x => new { x.ReservationId, x.VariantId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<InventoryPaymentReceipt>(b =>
        {
            b.ToTable("inventory_payment_receipts", "app", t => t.HasCheckConstraint("CK_inventory_payment_receipts_values", "amount >= 0 AND currency = 'PLN' AND outcome IN ('Paid','PaymentReview')"));
            b.HasKey(x => x.EventId);
            b.HasOne<InventoryOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Outcome).HasMaxLength(24);
        });
        foreach (var type in new[] { typeof(InventoryOrder), typeof(InventoryOrderItem), typeof(StockReservation),
                     typeof(StockReservationItem), typeof(StockMovement), typeof(InventoryPaymentReceipt) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));
    }
}
