using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "app",
                table: "promotions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "collection_id",
                schema: "app",
                table: "promotions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "app",
                table: "promotions",
                type: "text",
                nullable: false,
                defaultValue: "product");

            migrationBuilder.CreateTable(
                name: "collections",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.id);
                    table.CheckConstraint("CK_collections_name", "length(btrim(display_name)) BETWEEN 1 AND 120");
                    table.CheckConstraint("CK_collections_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("CK_collections_state", "status IN ('Draft','Published','Hidden') AND version > 0");
                });

            migrationBuilder.CreateTable(
                name: "collection_operations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_operations_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "app",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_products",
                schema: "app",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_products", x => new { x.collection_id, x.product_id });
                    table.ForeignKey(
                        name: "FK_collection_products_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "app",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_products_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "app",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_promotions_collection_id",
                schema: "app",
                table: "promotions",
                column: "collection_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions",
                sql: "(scope = 'product' AND product_id IS NOT NULL AND collection_id IS NULL) OR (scope = 'collection' AND product_id IS NULL AND collection_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_collection_operations_actor_user_id_operation_key",
                schema: "app",
                table: "collection_operations",
                columns: new[] { "actor_user_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_operations_collection_id",
                schema: "app",
                table: "collection_operations",
                column: "collection_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_products_product_id",
                schema: "app",
                table: "collection_products",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_collections_slug",
                schema: "app",
                table: "collections",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_promotions_collections_collection_id",
                schema: "app",
                table: "promotions",
                column: "collection_id",
                principalSchema: "app",
                principalTable: "collections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            InstallRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveRules(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_promotions_collections_collection_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropTable(
                name: "collection_operations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "collection_products",
                schema: "app");

            migrationBuilder.DropTable(
                name: "collections",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_promotions_collection_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_promotions_scope",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "collection_id",
                schema: "app",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "app",
                table: "promotions");

            migrationBuilder.AlterColumn<Guid>(
                name: "product_id",
                schema: "app",
                table: "promotions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
