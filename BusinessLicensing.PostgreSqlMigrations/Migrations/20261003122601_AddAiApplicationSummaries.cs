using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BusinessLicensing.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAiApplicationSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApplicationSummariesEnabled",
                table: "AiSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ApplicationAiSummaries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    GeneratedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FormatVersion = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationAiSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationAiSummaries_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AiSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ApplicationSummariesEnabled",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAiSummaries_ApplicationId_RevisionNumber",
                table: "ApplicationAiSummaries",
                columns: new[] { "ApplicationId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationAiSummaries");

            migrationBuilder.DropColumn(
                name: "ApplicationSummariesEnabled",
                table: "AiSettings");
        }
    }
}
