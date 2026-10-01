using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "abandoned_multipart_days",
                table: "system_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "orphan_grace_minutes",
                table: "system_settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "system_settings",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "abandoned_multipart_days", "orphan_grace_minutes" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "abandoned_multipart_days",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "orphan_grace_minutes",
                table: "system_settings");
        }
    }
}
