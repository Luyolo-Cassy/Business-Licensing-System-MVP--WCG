using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationDecisionReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecisionDateUtc",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "Applications",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionDateUtc",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "Applications");
        }
    }
}
