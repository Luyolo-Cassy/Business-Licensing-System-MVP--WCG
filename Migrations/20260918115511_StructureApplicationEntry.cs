using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class StructureApplicationEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicantAddressLine1",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantAddressLine2",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantCity",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantFirstName",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantLastName",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantPostalCode",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantSuburb",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalAddressLine1",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalAddressLine2",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PostalAddressSameAsBusiness",
                table: "ApplicationDetails",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCity",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalPostalCode",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalSuburb",
                table: "ApplicationDetails",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicantAddressLine1",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantAddressLine2",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantCity",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantFirstName",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantLastName",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantPostalCode",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "ApplicantSuburb",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalAddressLine1",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalAddressLine2",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalAddressSameAsBusiness",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalCity",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalPostalCode",
                table: "ApplicationDetails");

            migrationBuilder.DropColumn(
                name: "PostalSuburb",
                table: "ApplicationDetails");
        }
    }
}
