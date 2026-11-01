using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.AddColumn<Guid>(
                name: "price_reduction_id",
                schema: "app",
                table: "variant_price_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "promotion_operation_id",
                schema: "app",
                table: "variant_price_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promotions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    discount_type = table.Column<string>(type: "text", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.id);
                    table.CheckConstraint("CK_promotions_name", "length(btrim(name)) BETWEEN 1 AND 120");
                    table.CheckConstraint("CK_promotions_rule", "discount_type IN ('percentage','amount') AND discount_value > 0 AND (discount_type <> 'percentage' OR discount_value <= 100)");
                    table.CheckConstraint("CK_promotions_state", "status IN ('Draft','Active','Ended') AND version > 0 AND ((status = 'Draft' AND activated_at IS NULL AND ended_at IS NULL) OR (status = 'Active' AND activated_at IS NOT NULL AND ended_at IS NULL) OR (status = 'Ended' AND activated_at IS NOT NULL AND ended_at IS NOT NULL AND ended_at >= activated_at))");
                    table.ForeignKey(
                        name: "FK_promotions_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_offer_events",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    complete_from_birth = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variant_offer_events", x => x.id);
                    table.CheckConstraint("CK_variant_offer_events_source", "source IN ('database','import','demo')");
                    table.ForeignKey(
                        name: "FK_variant_offer_events_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_operations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: true),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_operations", x => x.id);
                    table.CheckConstraint("CK_promotion_operations_actor", "(actor_user_id IS NULL AND operation_key IS NULL AND kind = 'demo') OR (actor_user_id IS NOT NULL AND operation_key IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_promotion_operations_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "app",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_variants",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_variants", x => x.id);
                    table.CheckConstraint("CK_promotion_variants_dates", "ended_at IS NULL OR ended_at >= started_at");
                    table.ForeignKey(
                        name: "FK_promotion_variants_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_variants_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "app",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_reductions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prior_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_reductions", x => x.id);
                    table.CheckConstraint("CK_price_reductions_reference", "prior_amount >= 0 AND reference_start < started_at AND basis IN ('Full30Days','SinceFirstOffer')");
                    table.ForeignKey(
                        name: "FK_price_reductions_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_reductions_promotion_variants_promotion_variant_id",
                        column: x => x.promotion_variant_id,
                        principalSchema: "app",
                        principalTable: "promotion_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_price_reduction_id",
                schema: "app",
                table: "variant_price_changes",
                column: "price_reduction_id");

            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_promotion_operation_id",
                schema: "app",
                table: "variant_price_changes",
                column: "promotion_operation_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes",
                sql: "(promotion_operation_id IS NULL AND price_reduction_id IS NULL AND ((operation_key IS NULL AND changed_by_user_id IS NULL AND previous_price_change_id IS NULL) OR (operation_key IS NOT NULL AND changed_by_user_id IS NOT NULL))) OR (promotion_operation_id IS NOT NULL AND operation_key IS NULL AND changed_by_user_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_price_reductions_promotion_variant_id",
                schema: "app",
                table: "price_reductions",
                column: "promotion_variant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_reductions_variant_id",
                schema: "app",
                table: "price_reductions",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_operations_actor_user_id_operation_key",
                schema: "app",
                table: "promotion_operations",
                columns: new[] { "actor_user_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_operations_promotion_id",
                schema: "app",
                table: "promotion_operations",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_variants_promotion_id_variant_id",
                schema: "app",
                table: "promotion_variants",
                columns: new[] { "promotion_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_promotion_variants_variant_id",
                schema: "app",
                table: "promotion_variants",
                column: "variant_id",
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_product_id_status",
                schema: "app",
                table: "promotions",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_variant_offer_events_variant_id_occurred_at_id",
                schema: "app",
                table: "variant_offer_events",
                columns: new[] { "variant_id", "occurred_at", "id" });

            migrationBuilder.AddForeignKey(
                name: "FK_variant_price_changes_price_reductions_price_reduction_id",
                schema: "app",
                table: "variant_price_changes",
                column: "price_reduction_id",
                principalSchema: "app",
                principalTable: "price_reductions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_variant_price_changes_promotion_operations_promotion_operat~",
                schema: "app",
                table: "variant_price_changes",
                column: "promotion_operation_id",
                principalSchema: "app",
                principalTable: "promotion_operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            InstallOfferTracking(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveOfferTracking(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_variant_price_changes_price_reductions_price_reduction_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropForeignKey(
                name: "FK_variant_price_changes_promotion_operations_promotion_operat~",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropTable(
                name: "price_reductions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "promotion_operations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "variant_offer_events",
                schema: "app");

            migrationBuilder.DropTable(
                name: "promotion_variants",
                schema: "app");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_variant_price_changes_price_reduction_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropIndex(
                name: "IX_variant_price_changes_promotion_operation_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropColumn(
                name: "price_reduction_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropColumn(
                name: "promotion_operation_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes",
                sql: "(operation_key IS NULL AND changed_by_user_id IS NULL AND previous_price_change_id IS NULL) OR (operation_key IS NOT NULL AND changed_by_user_id IS NOT NULL)");
        }
    }
}
