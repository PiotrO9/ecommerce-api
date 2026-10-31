using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrators",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administrators", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_administrators_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.Sql("ALTER TABLE app.administrators ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("REVOKE ALL ON TABLE app.administrators FROM PUBLIC, anon, authenticated, service_role;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrators",
                schema: "app");
        }
    }
}
