using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipartUploadCustomMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_metadata",
                table: "multipart_uploads",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_metadata",
                table: "multipart_uploads");
        }
    }
}
