using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramExtension : Migration
    {
        // pg_trgm for the object search index, which SearchIndexBuilder builds after start: on millions of keys the build
        // outlasts a migration's command timeout. Best effort: a role that may not create extensions still starts.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS pg_trgm;
                EXCEPTION WHEN OTHERS THEN
                    RAISE WARNING 'Object search runs without its trigram index: %', SQLERRM;
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
