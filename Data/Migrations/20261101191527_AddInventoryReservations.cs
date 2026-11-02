using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_variant_stock_variant_id",
                schema: "app",
                table: "variant_stock",
                column: "variant_id");

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    goods_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.CheckConstraint("CK_orders_status", "status IN ('PendingPayment','Paid','Cancelled','Expired','PaymentReview')");
                    table.CheckConstraint("CK_orders_total", "goods_total >= 0 AND currency = 'PLN'");
                    table.ForeignKey(
                        name: "FK_orders_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_payment_receipts",
                schema: "app",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_payment_receipts", x => x.event_id);
                    table.CheckConstraint("CK_inventory_payment_receipts_values", "amount >= 0 AND currency = 'PLN' AND outcome IN ('Paid','PaymentReview')");
                    table.ForeignKey(
                        name: "FK_inventory_payment_receipts_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "app",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "app",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    attributes_json = table.Column<string>(type: "jsonb", nullable: false),
                    price_change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => new { x.order_id, x.variant_id });
                    table.CheckConstraint("CK_order_items_values", "quantity BETWEEN 1 AND 99 AND unit_price >= 0");
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "app",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_items_variant_price_changes_price_change_id",
                        column: x => x.price_change_id,
                        principalSchema: "app",
                        principalTable: "variant_price_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservations", x => x.id);
                    table.CheckConstraint("CK_stock_reservations_dates", "expires_at > created_at");
                    table.CheckConstraint("CK_stock_reservations_status", "status IN ('Active','Consumed','Released','Expired')");
                    table.ForeignKey(
                        name: "FK_stock_reservations_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "app",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_reservation_items",
                schema: "app",
                columns: table => new
                {
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_reservation_items", x => new { x.reservation_id, x.variant_id });
                    table.CheckConstraint("CK_stock_reservation_items_quantity", "quantity BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_stock_reservation_items_stock_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "app",
                        principalTable: "stock_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_reservation_items_variant_stock_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "variant_stock",
                        principalColumn: "variant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_before = table.Column<int>(type: "integer", nullable: false),
                    quantity_after = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                    table.CheckConstraint("CK_stock_movements_quantity", "quantity_before > quantity_after AND quantity_after >= 0");
                    table.ForeignKey(
                        name: "FK_stock_movements_stock_reservation_items_reservation_id_vari~",
                        columns: x => new { x.reservation_id, x.variant_id },
                        principalSchema: "app",
                        principalTable: "stock_reservation_items",
                        principalColumns: new[] { "reservation_id", "variant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_payment_receipts_order_id",
                schema: "app",
                table: "inventory_payment_receipts",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_price_change_id",
                schema: "app",
                table: "order_items",
                column: "price_change_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_variant_id",
                schema: "app",
                table: "order_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id_created_at_id",
                schema: "app",
                table: "orders",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_user_id_operation_key",
                schema: "app",
                table: "orders",
                columns: new[] { "user_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_reservation_id_variant_id",
                schema: "app",
                table: "stock_movements",
                columns: new[] { "reservation_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservation_items_variant_id_reservation_id",
                schema: "app",
                table: "stock_reservation_items",
                columns: new[] { "variant_id", "reservation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_expires_at",
                schema: "app",
                table: "stock_reservations",
                column: "expires_at",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_stock_reservations_order_id",
                schema: "app",
                table: "stock_reservations",
                column: "order_id",
                unique: true);
            InstallInventoryRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveInventoryRules(migrationBuilder);
            migrationBuilder.DropTable(
                name: "inventory_payment_receipts",
                schema: "app");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_reservation_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "stock_reservations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "app");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_variant_stock_variant_id",
                schema: "app",
                table: "variant_stock");
        }
    }
}
