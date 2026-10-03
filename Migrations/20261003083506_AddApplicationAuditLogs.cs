using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Municipality = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    NewStatus = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
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
