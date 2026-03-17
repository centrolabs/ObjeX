using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    presigned_url_default_expiry_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    presigned_url_max_expiry_seconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "system_settings",
                columns: new[] { "id", "presigned_url_default_expiry_seconds", "presigned_url_max_expiry_seconds" },
                values: new object[] { 1, 3600, 604800 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_settings");
        }
    }
}
