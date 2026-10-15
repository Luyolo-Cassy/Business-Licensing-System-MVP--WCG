using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessLicensing_Practice.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentDocumentValidationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CertificateOfIncorporationValidationEnabled",
                table: "AiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ProofOfAddressValidationEnabled",
                table: "AiSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AiSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CertificateOfIncorporationValidationEnabled", "ProofOfAddressValidationEnabled" },
                values: new object[] { false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateOfIncorporationValidationEnabled",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "ProofOfAddressValidationEnabled",
                table: "AiSettings");
        }
    }
}
