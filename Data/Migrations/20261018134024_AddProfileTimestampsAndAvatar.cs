using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace E_commerce_backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileTimestampsAndAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                schema: "app",
                table: "user_profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                schema: "app",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                schema: "app",
                table: "user_profiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.Sql("""
                CREATE FUNCTION app.set_user_profile_updated_at()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    NEW.updated_at := CURRENT_TIMESTAMP;
                    RETURN NEW;
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER set_user_profile_updated_at
                BEFORE UPDATE ON app.user_profiles
                FOR EACH ROW
                EXECUTE FUNCTION app.set_user_profile_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER set_user_profile_updated_at ON app.user_profiles;");
            migrationBuilder.Sql("DROP FUNCTION app.set_user_profile_updated_at();");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                schema: "app",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "app",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "app",
                table: "user_profiles");
        }
    }
}
