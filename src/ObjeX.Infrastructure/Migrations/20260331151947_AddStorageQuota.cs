using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "default_storage_quota_bytes",
                table: "system_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "storage_quota_bytes",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "system_settings",
                keyColumn: "id",
                keyValue: 1,
                column: "default_storage_quota_bytes",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_storage_quota_bytes",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "storage_quota_bytes",
                table: "AspNetUsers");
        }
    }
}
