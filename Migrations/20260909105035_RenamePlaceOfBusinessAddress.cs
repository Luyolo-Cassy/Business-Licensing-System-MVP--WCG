using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlaceOfBusinessAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "BusinessAddress", table: "Applications", newName: "PlaceOfBusinessAddress");
            migrationBuilder.RenameColumn(name: "AddressLine1", table: "Applications", newName: "PlaceOfBusinessAddressLine1");
            migrationBuilder.RenameColumn(name: "AddressLine2", table: "Applications", newName: "PlaceOfBusinessAddressLine2");
            migrationBuilder.RenameColumn(name: "Suburb", table: "Applications", newName: "PlaceOfBusinessSuburb");
            migrationBuilder.RenameColumn(name: "City", table: "Applications", newName: "PlaceOfBusinessCity");
            migrationBuilder.RenameColumn(name: "PostalCode", table: "Applications", newName: "PlaceOfBusinessPostalCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessAddress", table: "Applications", newName: "BusinessAddress");
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessAddressLine1", table: "Applications", newName: "AddressLine1");
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessAddressLine2", table: "Applications", newName: "AddressLine2");
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessSuburb", table: "Applications", newName: "Suburb");
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessCity", table: "Applications", newName: "City");
            migrationBuilder.RenameColumn(name: "PlaceOfBusinessPostalCode", table: "Applications", newName: "PostalCode");
        }
    }
}