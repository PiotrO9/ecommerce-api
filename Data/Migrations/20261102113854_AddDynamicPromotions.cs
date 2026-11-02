using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_promotion_variants_promotion_id_variant_id",
                schema: "app",
                table: "promotion_variants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_operations_actor",
                schema: "app",
                table: "promotion_operations");

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "app",
                table: "promotions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "promotion_conflicts",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_promotion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_promotion_conflicts_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_conflicts_promotions_other_promotion_id",
                        column: x => x.other_promotion_id,
                        principalSchema: "app",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_conflicts_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "app",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_exclusions",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_exclusions", x => x.id);
                    table.CheckConstraint("CK_promotion_exclusions_target", "(product_id IS NULL) <> (variant_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_promotion_exclusions_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_exclusions_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_promotion_exclusions_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "app",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_reconciliation",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    dirty = table.Column<bool>(type: "boolean", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_reconciliation", x => x.id);
                    table.CheckConstraint("CK_promotion_reconciliation_singleton", "id=1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_category_id",
                schema: "app",
                table: "promotions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotions_scope_status",
                schema: "app",
                table: "promotions",
                columns: new[] { "scope", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions",
                sql: "(scope='product' AND product_id IS NOT NULL AND collection_id IS NULL AND category_id IS NULL) OR (scope='collection' AND product_id IS NULL AND collection_id IS NOT NULL AND category_id IS NULL) OR (scope='catalog' AND product_id IS NULL AND collection_id IS NULL AND category_id IS NULL) OR (scope='category' AND product_id IS NULL AND collection_id IS NULL AND category_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_variants_promotion_id_variant_id",
                schema: "app",
                table: "promotion_variants",
                columns: new[] { "promotion_id", "variant_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_operations_actor",
                schema: "app",
                table: "promotion_operations",
                sql: "(actor_user_id IS NULL AND operation_key IS NULL AND kind IN ('demo','dynamic_reconcile')) OR (actor_user_id IS NOT NULL AND operation_key IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_conflicts_other_promotion_id",
                schema: "app",
                table: "promotion_conflicts",
                column: "other_promotion_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_conflicts_promotion_id_variant_id_code",
                schema: "app",
                table: "promotion_conflicts",
                columns: new[] { "promotion_id", "variant_id", "code" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_conflicts_resolved_at_updated_at",
                schema: "app",
                table: "promotion_conflicts",
                columns: new[] { "resolved_at", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_promotion_conflicts_variant_id",
                schema: "app",
                table: "promotion_conflicts",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_exclusions_product_id",
                schema: "app",
                table: "promotion_exclusions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_exclusions_promotion_id_product_id",
                schema: "app",
                table: "promotion_exclusions",
                columns: new[] { "promotion_id", "product_id" },
                unique: true,
                filter: "product_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_exclusions_promotion_id_variant_id",
                schema: "app",
                table: "promotion_exclusions",
                columns: new[] { "promotion_id", "variant_id" },
                unique: true,
                filter: "variant_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_exclusions_variant_id",
                schema: "app",
                table: "promotion_exclusions",
                column: "variant_id");

            migrationBuilder.AddForeignKey(
                name: "FK_promotions_categories_category_id",
                schema: "app",
                table: "promotions",
                column: "category_id",
                principalSchema: "app",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            InstallDynamicRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveDynamicRules(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_promotions_categories_category_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropTable(
                name: "promotion_conflicts",
                schema: "app");

            migrationBuilder.DropTable(
                name: "promotion_exclusions",
                schema: "app");

            migrationBuilder.DropTable(
                name: "promotion_reconciliation",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_promotions_category_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_promotions_scope_status",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropIndex(
                name: "IX_promotion_variants_promotion_id_variant_id",
                schema: "app",
                table: "promotion_variants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotion_operations_actor",
                schema: "app",
                table: "promotion_operations");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions",
                sql: "(scope = 'product' AND product_id IS NOT NULL AND collection_id IS NULL) OR (scope = 'collection' AND product_id IS NULL AND collection_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_promotion_variants_promotion_id_variant_id",
                schema: "app",
                table: "promotion_variants",
                columns: new[] { "promotion_id", "variant_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotion_operations_actor",
                schema: "app",
                table: "promotion_operations",
                sql: "(actor_user_id IS NULL AND operation_key IS NULL AND kind = 'demo') OR (actor_user_id IS NOT NULL AND operation_key IS NOT NULL)");
        }
    }
}
