using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPublicationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            InstallPublicationRules(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemovePublicationRules(migrationBuilder);
        }
    }
}
