using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessLicensing.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Municipality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditLogs_ActorDisplayName",
                table: "ApplicationAuditLogs",
                column: "ActorDisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditLogs_ApplicationNumber",
                table: "ApplicationAuditLogs",
                column: "ApplicationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditLogs_EventType",
                table: "ApplicationAuditLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditLogs_Municipality",
                table: "ApplicationAuditLogs",
                column: "Municipality");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditLogs_OccurredAtUtc",
                table: "ApplicationAuditLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationAuditLogs");
        }
    }
}
