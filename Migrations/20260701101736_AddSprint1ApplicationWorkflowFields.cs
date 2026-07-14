using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddSprint1ApplicationWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationFormFileName",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationFormFilePath",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BusinessCategory",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EntertainmentActivityType",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FoodHandlingType",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PopiaConsentAccepted",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Suburb",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TradingName",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ApplicationFormFileName",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ApplicationFormFilePath",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "BusinessCategory",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "EntertainmentActivityType",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "FoodHandlingType",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "PopiaConsentAccepted",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "Suburb",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "TradingName",
                table: "Applications");
        }
    }
}
