using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buckets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    object_count = table.Column<long>(type: "INTEGER", nullable: false),
                    total_size = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buckets", x => x.id);
                    table.UniqueConstraint("ak_buckets_name", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "blob_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    bucket_name = table.Column<string>(type: "TEXT", nullable: false),
                    key = table.Column<string>(type: "TEXT", nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: false),
                    e_tag = table.Column<string>(type: "TEXT", nullable: false),
                    storage_path = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blob_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_blob_objects_buckets_bucket_name",
                        column: x => x.bucket_name,
                        principalTable: "buckets",
                        principalColumn: "name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blob_objects_bucket_name_key",
                table: "blob_objects",
                columns: new[] { "bucket_name", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_buckets_name",
                table: "buckets",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blob_objects");

            migrationBuilder.DropTable(
                name: "buckets");
        }
    }
}
