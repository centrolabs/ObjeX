using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectKeyTrigramIndex : Migration
    {
        // Lets the object search use an index for LIKE '%term%' on lower(key). Best effort: a role that may not create the
        // extension still starts, and search falls back to a scan.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS pg_trgm;
                    CREATE INDEX IF NOT EXISTS ix_blob_objects_key_trgm ON blob_objects USING gin (lower(key) gin_trgm_ops);
                EXCEPTION WHEN OTHERS THEN
                    RAISE WARNING 'Object search runs without the trigram index: %', SQLERRM;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_blob_objects_key_trgm;");
        }
    }
}
