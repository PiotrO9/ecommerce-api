using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "app",
                table: "variant_stock",
                type: "bigint",
                rowVersion: true,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "app",
                table: "products",
                type: "bigint",
                rowVersion: true,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "app",
                table: "product_variants",
                type: "bigint",
                rowVersion: true,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "app",
                table: "categories",
                type: "bigint",
                rowVersion: true,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "app",
                table: "attribute_keys",
                type: "bigint",
                rowVersion: true,
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "catalog_operations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    expected_version = table.Column<long>(type: "bigint", nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    quantity_before = table.Column<int>(type: "integer", nullable: true),
                    quantity_after = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_operations", x => x.id);
                    table.CheckConstraint("CK_catalog_operations_kind", "kind IN ('product.create','product.update','product.categories','product.status','variant.create','variant.update','variant.attributes','variant.status','category.create','category.update','category.parent','category.delete','key.create','key.update','pair.create','stock.set')");
                    table.CheckConstraint("CK_catalog_operations_status", "status_code IN (200,201,204)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalog_operations_actor_user_id_operation_key",
                schema: "app",
                table: "catalog_operations",
                columns: new[] { "actor_user_id", "operation_key" },
                unique: true);
            InstallManagementRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveManagementRules(migrationBuilder);
            migrationBuilder.DropTable(
                name: "catalog_operations",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "variant_stock");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "products");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "app",
                table: "attribute_keys");
        }
    }
}
