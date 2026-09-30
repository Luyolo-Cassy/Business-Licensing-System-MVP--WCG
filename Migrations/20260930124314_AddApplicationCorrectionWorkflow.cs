using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationCorrectionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationDrafts_UserId",
                table: "ApplicationDrafts");

            migrationBuilder.AddColumn<string>(
                name: "MessageType",
                table: "MunicipalMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "MunicipalMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResubmittedAtUtc",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SourceApplicationId",
                table: "ApplicationDrafts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDrafts_SourceApplicationId",
                table: "ApplicationDrafts",
                column: "SourceApplicationId",
                unique: true,
                filter: "\"SourceApplicationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDrafts_UserId",
                table: "ApplicationDrafts",
                column: "UserId",
                unique: true,
                filter: "\"SourceApplicationId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationDrafts_Applications_SourceApplicationId",
                table: "ApplicationDrafts",
                column: "SourceApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationDrafts_Applications_SourceApplicationId",
                table: "ApplicationDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationDrafts_SourceApplicationId",
                table: "ApplicationDrafts");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationDrafts_UserId",
                table: "ApplicationDrafts");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "MunicipalMessages");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "MunicipalMessages");

            migrationBuilder.DropColumn(
                name: "LastResubmittedAtUtc",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "SourceApplicationId",
                table: "ApplicationDrafts");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDrafts_UserId",
                table: "ApplicationDrafts",
                column: "UserId",
                unique: true);
        }
    }
}
