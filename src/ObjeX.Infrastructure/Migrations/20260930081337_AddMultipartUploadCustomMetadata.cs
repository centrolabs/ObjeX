using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
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
                type: "TEXT",
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
