using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStep = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSavedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDraftDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationDraftId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentType = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    AiValidationStatus = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDraftDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDraftDocuments_ApplicationDrafts_ApplicationDraftId",
                        column: x => x.ApplicationDraftId,
                        principalTable: "ApplicationDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDraftDocuments_ApplicationDraftId_DocumentType",
                table: "ApplicationDraftDocuments",
                columns: new[] { "ApplicationDraftId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDrafts_UserId",
                table: "ApplicationDrafts",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationDraftDocuments");

            migrationBuilder.DropTable(
                name: "ApplicationDrafts");
        }
    }
}
