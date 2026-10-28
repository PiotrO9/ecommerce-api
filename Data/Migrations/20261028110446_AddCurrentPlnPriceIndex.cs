using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentPlnPriceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_variant_price_changes_current_pln",
                schema: "app",
                table: "variant_price_changes",
                columns: new[] { "variant_id", "effective_at", "created_at", "id" },
                descending: new[] { false, true, true, true },
                filter: "currency = 'PLN'")
                .Annotation("Npgsql:IndexInclude", new[] { "selling_amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_variant_price_changes_current_pln",
                schema: "app",
                table: "variant_price_changes");
        }
    }
}
