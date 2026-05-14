using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessApplicationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessAddress",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RegistrationNumber",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessAddress",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RegistrationNumber",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "Applications");
        }
    }
}
