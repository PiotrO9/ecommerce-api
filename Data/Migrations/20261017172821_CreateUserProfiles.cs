using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "user_profiles",
                schema: "app",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // API aplikacji korzysta z EF i połączenia PostgreSQL. Nie udostępniamy
            // profili bezpośrednio rolom używanym przez Supabase Data API.
            migrationBuilder.Sql("ALTER TABLE app.user_profiles ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("REVOKE ALL ON TABLE app.user_profiles FROM PUBLIC, anon, authenticated, service_role;");
            migrationBuilder.Sql("ALTER TABLE public.\"__EcommerceMigrationsHistory\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("REVOKE ALL ON TABLE public.\"__EcommerceMigrationsHistory\" FROM PUBLIC, anon, authenticated, service_role;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_profiles",
                schema: "app");
        }
    }
}
