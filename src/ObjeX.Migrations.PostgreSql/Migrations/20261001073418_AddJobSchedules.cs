using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ObjeX.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddJobSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_schedules",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "text", nullable: false),
                    cron = table.Column<string>(type: "text", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_schedules", x => x.job_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_schedules");
        }
    }
}
