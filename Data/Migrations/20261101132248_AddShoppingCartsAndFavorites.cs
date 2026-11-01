using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingCartsAndFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "favorite_lists",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorite_lists", x => x.user_id);
                    table.CheckConstraint("CK_favorite_lists_version", "version >= 0");
                    table.ForeignKey(
                        name: "FK_favorite_lists_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopping_states",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cart_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_states", x => x.id);
                    table.CheckConstraint("CK_shopping_states_owner", "(user_id IS NOT NULL AND guest_token_hash IS NULL AND expires_at IS NULL) OR (user_id IS NULL AND guest_token_hash IS NOT NULL AND guest_token_hash ~ '^[0-9A-F]{64}$' AND expires_at IS NOT NULL)");
                    table.CheckConstraint("CK_shopping_states_version", "cart_version >= 0");
                    table.ForeignKey(
                        name: "FK_shopping_states_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "favorite_items",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_favorite_items", x => new { x.user_id, x.variant_id });
                    table.ForeignKey(
                        name: "FK_favorite_items_favorite_lists_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "favorite_lists",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_favorite_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cart_items",
                schema: "app",
                columns: table => new
                {
                    shopping_state_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cart_items", x => new { x.shopping_state_id, x.variant_id });
                    table.CheckConstraint("CK_cart_items_quantity", "quantity BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "FK_cart_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "app",
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cart_items_shopping_states_shopping_state_id",
                        column: x => x.shopping_state_id,
                        principalSchema: "app",
                        principalTable: "shopping_states",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cart_items_variant_id",
                schema: "app",
                table: "cart_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_favorite_items_user_id_created_at_variant_id",
                schema: "app",
                table: "favorite_items",
                columns: new[] { "user_id", "created_at", "variant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_favorite_items_variant_id",
                schema: "app",
                table: "favorite_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_favorite_lists_scope_id",
                schema: "app",
                table: "favorite_lists",
                column: "scope_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shopping_states_expires_at",
                schema: "app",
                table: "shopping_states",
                column: "expires_at",
                filter: "expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_states_guest_token_hash",
                schema: "app",
                table: "shopping_states",
                column: "guest_token_hash",
                unique: true,
                filter: "guest_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_states_user_id",
                schema: "app",
                table: "shopping_states",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");
            InstallShoppingRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveShoppingRules(migrationBuilder);
            migrationBuilder.DropTable(
                name: "cart_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "favorite_items",
                schema: "app");

            migrationBuilder.DropTable(
                name: "shopping_states",
                schema: "app");

            migrationBuilder.DropTable(
                name: "favorite_lists",
                schema: "app");
        }
    }
}
