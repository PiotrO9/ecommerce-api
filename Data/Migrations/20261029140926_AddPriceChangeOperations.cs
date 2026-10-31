using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceChangeOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "changed_by_user_id",
                schema: "app",
                table: "variant_price_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "operation_key",
                schema: "app",
                table: "variant_price_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_price_change_id",
                schema: "app",
                table: "variant_price_changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_changed_by_user_id_operation_key",
                schema: "app",
                table: "variant_price_changes",
                columns: new[] { "changed_by_user_id", "operation_key" },
                unique: true,
                filter: "operation_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_previous_price_change_id",
                schema: "app",
                table: "variant_price_changes",
                column: "previous_price_change_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes",
                sql: "(operation_key IS NULL AND changed_by_user_id IS NULL AND previous_price_change_id IS NULL) OR (operation_key IS NOT NULL AND changed_by_user_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_variant_price_changes_variant_price_changes_previous_price_~",
                schema: "app",
                table: "variant_price_changes",
                column: "previous_price_change_id",
                principalSchema: "app",
                principalTable: "variant_price_changes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_variant_price_changes_variant_price_changes_previous_price_~",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropIndex(
                name: "IX_variant_price_changes_changed_by_user_id_operation_key",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropIndex(
                name: "IX_variant_price_changes_previous_price_change_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_variant_price_changes_operation",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropColumn(
                name: "changed_by_user_id",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropColumn(
                name: "operation_key",
                schema: "app",
                table: "variant_price_changes");

            migrationBuilder.DropColumn(
                name: "previous_price_change_id",
                schema: "app",
                table: "variant_price_changes");
        }
    }
}
